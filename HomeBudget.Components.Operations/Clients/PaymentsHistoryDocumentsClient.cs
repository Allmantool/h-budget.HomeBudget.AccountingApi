﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Extensions;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentsHistoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
    : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.PaymentsHistory),
        IPaymentsHistoryDocumentsClient
    {
        public async Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountId, FinancialPeriod period = null)
        {
            if (period != null)
            {
                var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(period.ToFinancialMonthIdentifier(accountId));

                return await targetCollection.Find(_ => true).ToListAsync();
            }

            var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);

            return await FilterByAsync(targetCollections, new ExpressionFilterDefinition<PaymentHistoryDocument>(_ => true));
        }

        public async Task<PaymentHistoryDocument> GetLastForPeriodAsync(string financialPeriodIdentifier)
        {
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            return await targetCollection.Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                .SortByDescending(f => f.Payload.Record.OperationDay)
                .Limit(1)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<PaymentHistoryDocument>> GetAllPeriodBalancesForAccountAsync(Guid accountId)
        {
            var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);
            var tasks = targetCollections.Select(cl => cl.Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                .SortByDescending(f => f.Payload.Record.OperationDay)
                .Limit(1)
                .FirstOrDefaultAsync());

            return await Task.WhenAll(tasks);
        }

        public async Task<PaymentHistoryDocument> GetByIdAsync(Guid accountId, Guid operationId)
        {
            var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);
            var payload = await FilterByAsync(targetCollections, new ExpressionFilterDefinition<PaymentHistoryDocument>(d => d.Payload.Record.Key == operationId));

            return payload.SingleOrDefault();
        }

        public async Task InsertOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload)
        {
            var document = new PaymentHistoryDocument { Payload = payload };
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            await targetCollection.InsertOneAsync(document);
        }

        public async Task ReplaceOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload)
        {
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);
            var filter = Builders<PaymentHistoryDocument>.Filter.Eq(d => d.Payload.Record.Key, payload.Record.Key);
            var document = new PaymentHistoryDocument
            {
                Payload = payload
            };

            await targetCollection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions
                {
                    IsUpsert = true
                });
        }

        public async Task BulkWriteAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> payload)
        {
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            var bulkOps = new List<WriteModel<PaymentHistoryDocument>>();

            foreach (var record in payload)
            {
                var existingDocument = await targetCollection
                    .Find(Builders<PaymentHistoryDocument>.Filter.Eq(d => d.Payload.Record.Key, record.Record.Key))
                    .FirstOrDefaultAsync();

                var newDocument = new PaymentHistoryDocument
                {
                    Id = existingDocument?.Id ?? record.Record.Key.ToObjectId(),
                    Payload = record
                };

                bulkOps.Add(new ReplaceOneModel<PaymentHistoryDocument>(
                    Builders<PaymentHistoryDocument>.Filter.Eq(d => d.Payload.Record.Key, record.Record.Key),
                    newDocument)
                {
                    IsUpsert = true
                });
            }

            if (bulkOps.Count > 0)
            {
                await targetCollection.BulkWriteAsync(bulkOps);
            }
        }

        public async Task RemoveAsync(string financialPeriodIdentifier)
        {
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);
            await targetCollection.DeleteManyAsync(_ => true);
        }

        public async Task RewriteAllAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords)
        {
            await RemoveAsync(financialPeriodIdentifier);
            await BulkWriteAsync(financialPeriodIdentifier, operationHistoryRecords);
        }

        private async Task<IEnumerable<IMongoCollection<PaymentHistoryDocument>>> GetPaymentAccountCollectionsAsync(Guid accountId)
        {
            var databaseCollectionNames = await MongoDatabase.ListCollectionNamesAsync();
            var dbCollections = await databaseCollectionNames.ToListAsync();
            var paymentAccountCollections = dbCollections.Where(name => name.StartsWith(accountId.ToString()));

            return paymentAccountCollections.Select(collectionName => MongoDatabase.GetCollection<PaymentHistoryDocument>(collectionName));
        }

        private async Task<IMongoCollection<PaymentHistoryDocument>> GetPaymentAccountCollectionForPeriodAsync(string financialPeriodIdentifier)
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryDocument>(financialPeriodIdentifier);
            var indexKeysDefinition = Builders<PaymentHistoryDocument>.IndexKeys.Ascending(p => p.Payload.Record.Key);
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentHistoryDocument>(indexKeysDefinition));

            return collection;
        }

        private static async Task<IReadOnlyCollection<PaymentHistoryDocument>> FilterByAsync(
            IEnumerable<IMongoCollection<PaymentHistoryDocument>> collections,
            FilterDefinition<PaymentHistoryDocument> filter)
        {
            var tasks = collections.Select(async collection => await collection.Find(filter).ToListAsync());
            var results = await Task.WhenAll(tasks);

            return results.SelectMany(docs => docs).ToList().AsReadOnly();
        }
    }
}
