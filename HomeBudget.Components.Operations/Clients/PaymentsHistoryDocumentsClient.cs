using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentsHistoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
        : BaseDocumentClient(dbOptions.Value.ConnectionString, dbOptions.Value.PaymentsHistoryDatabaseName),
            IPaymentsHistoryDocumentsClient
    {
        public async Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountId, FinancialPeriod period = null)
        {
            if (period != null)
            {
                var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(period.ToFinancialMonthIdentifier(accountId));

                var payload = await targetCollection.FindAsync(_ => true);

                return await payload.ToListAsync();
            }

            var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);

            return await FilterByAsync(targetCollections, new ExpressionFilterDefinition<PaymentHistoryDocument>(_ => true));
        }

        public async Task<PaymentHistoryDocument> GetLastForPeriodAsync(string financialPeriodIdentifier)
        {
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            return await targetCollection
                    .Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                    .SortByDescending(f => f.Payload.Record.OperationDay)
                    .Limit(1)
                    .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<PaymentHistoryDocument>> GetAllPeriodBalancesForAccountAsync(Guid accountId)
        {
            var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);

            var finalBalanceForPeriodTasks = targetCollections.Select(cl =>
            {
                return cl
                    .Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                    .SortByDescending(f => f.Payload.Record.OperationDay)
                    .Limit(1)
                    .FirstOrDefaultAsync();
            });

            return await Task.WhenAll(finalBalanceForPeriodTasks);
        }

        public async Task<PaymentHistoryDocument> GetByIdAsync(Guid accountId, Guid operationId)
        {
            var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);

            var payload = await FilterByAsync(targetCollections, new ExpressionFilterDefinition<PaymentHistoryDocument>(d => d.Payload.Record.Key.CompareTo(operationId) == 0));

            return payload.SingleOrDefault();
        }

        public async Task InsertOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload)
        {
            var document = new PaymentHistoryDocument
            {
                Payload = new PaymentOperationHistoryRecord
                {
                    Record = payload.Record,
                    Balance = payload.Balance
                }
            };

            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            await targetCollection.InsertOneAsync(document);
        }

        public async Task RemoveAsync(string financialPeriodIdentifier)
        {
            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            await targetCollection.DeleteManyAsync(_ => true);
        }

        public async Task RewriteAllAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords)
        {
            await RemoveAsync(financialPeriodIdentifier);

            var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

            var documents = operationHistoryRecords.Select(r => new PaymentHistoryDocument
            {
                Payload = new PaymentOperationHistoryRecord
                {
                    Record = r.Record,
                    Balance = r.Balance
                }
            });

            await targetCollection.InsertManyAsync(documents);
        }

        private async Task<IEnumerable<IMongoCollection<PaymentHistoryDocument>>> GetPaymentAccountCollectionsAsync(Guid accountId)
        {
            var dbCollections = await (await MongoDatabase.ListCollectionNamesAsync()).ToListAsync();

            var paymentAccountCollections = dbCollections.Where(name => name.StartsWith(accountId.ToString()));

            return paymentAccountCollections.Select(collectionName => MongoDatabase.GetCollection<PaymentHistoryDocument>(collectionName));
        }

        private async Task<IMongoCollection<PaymentHistoryDocument>> GetPaymentAccountCollectionForPeriodAsync(string financialPeriodIdentifier)
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryDocument>(financialPeriodIdentifier);

            var collectionIndexes = await collection.Indexes.ListAsync();

            if (await collectionIndexes.AnyAsync())
            {
                return collection;
            }

            var indexKeysDefinition = Builders<PaymentHistoryDocument>.IndexKeys
                .Ascending(paymentsHistory => paymentsHistory.Payload.Record.Key);

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentHistoryDocument>(indexKeysDefinition));

            return collection;
        }

        private static async Task<IReadOnlyCollection<PaymentHistoryDocument>> FilterByAsync(
            IEnumerable<IMongoCollection<PaymentHistoryDocument>> collections,
            FilterDefinition<PaymentHistoryDocument> filter)
        {
            var retrievalTasks = collections.Select(async collection =>
            {
                var payload = await collection.FindAsync(filter);
                return await payload.ToListAsync();
            });

            var allDocuments = (await Task.WhenAll(retrievalTasks))
                .SelectMany(docs => docs)
                .ToList();

            return allDocuments.AsReadOnly();
        }
    }
}
