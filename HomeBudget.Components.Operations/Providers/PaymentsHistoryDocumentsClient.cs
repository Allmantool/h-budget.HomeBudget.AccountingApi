using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Models;

namespace HomeBudget.Components.Operations.Providers
{
    internal class PaymentsHistoryDocumentsClient : IPaymentsHistoryDocumentsClient
    {
        private readonly IMongoDatabase _mongoDatabase;

        public PaymentsHistoryDocumentsClient(IOptions<PaymentsHistoryDbOptions> dbOptions)
        {
            var client = new MongoClient(dbOptions.Value.ConnectionString);

            _mongoDatabase = client.GetDatabase(dbOptions.Value.DatabaseName);
        }

        public async Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountingId)
        {
            var targetCollection = await GetPaymentAccountCollectionAsync(accountingId);

            return await targetCollection.Find(_ => true).ToListAsync();
        }

        public async Task InsertOneAsync(Guid accountingId, PaymentOperationHistoryRecord payload)
        {
            var document = new PaymentHistoryDocument
            {
                Record = payload.Record,
                Balance = payload.Balance
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
            var collection = _mongoDatabase.GetCollection<PaymentHistoryDocument>(accountingId.ToString());

            var collectionIndexes = await collection.Indexes.ListAsync();

            if (await collectionIndexes.AnyAsync())
            {
                return collection;
            }

            var indexKeysDefinition = Builders<PaymentHistoryDocument>.IndexKeys.Ascending(paymentsHistory => paymentsHistory.Record.Key);

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentHistoryDocument>(indexKeysDefinition));

            return collection;
        }
    }
}
