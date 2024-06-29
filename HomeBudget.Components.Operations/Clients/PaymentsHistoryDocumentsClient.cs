using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

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
        public async Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountingId)
        {
            var targetCollection = await GetPaymentAccountCollectionAsync(accountingId);

            var payload = await targetCollection.FindAsync(_ => true);

            return await payload.ToListAsync();
        }

        public async Task<PaymentHistoryDocument> GetByIdAsync(Guid accountingId, Guid operationId)
        {
            var targetCollection = await GetPaymentAccountCollectionAsync(accountingId);

            var payload = await targetCollection.FindAsync(d => d.Payload.Record.Key.CompareTo(operationId) == 0);

            return await payload.SingleOrDefaultAsync();
        }

        public async Task InsertOneAsync(Guid accountingId, PaymentOperationHistoryRecord payload)
        {
            var document = new PaymentHistoryDocument
            {
                Payload = new PaymentOperationHistoryRecord
                {
                    Record = payload.Record,
                    Balance = payload.Balance
                }
            };

            var targetCollection = await GetPaymentAccountCollectionAsync(accountingId);

            await targetCollection.InsertOneAsync(document);
        }

        public async Task RemoveAsync(Guid accountingId)
        {
            var targetCollection = await GetPaymentAccountCollectionAsync(accountingId);

            await targetCollection.DeleteManyAsync(_ => true);
        }

        public async Task RewriteAllAsync(Guid accountingId, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords)
        {
            await RemoveAsync(accountingId);

            foreach (var historyRecord in operationHistoryRecords)
            {
                await InsertOneAsync(accountingId, historyRecord);
            }
        }

        private async Task<IMongoCollection<PaymentHistoryDocument>> GetPaymentAccountCollectionAsync(Guid accountingId)
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryDocument>(accountingId.ToString());

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
    }
}
