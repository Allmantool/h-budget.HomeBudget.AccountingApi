using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;

namespace HomeBudget.Components.Accounts.Clients
{
    internal class PaymentAccountDocumentClient(IOptions<MongoDbOptions> dbOptions)
        : BaseDocumentClient(dbOptions.Value.ConnectionString, dbOptions.Value.PaymentAccountsDatabaseName),
        IPaymentAccountDocumentClient
    {
        public async Task<Result<IReadOnlyCollection<PaymentAccountDocument>>> GetAsync()
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var payload = await targetCollection.FindAsync(_ => true);

            return new Result<IReadOnlyCollection<PaymentAccountDocument>>(await payload.ToListAsync());
        }

        public async Task<Result<PaymentAccountDocument>> GetByIdAsync(string paymentAccountId)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Payload.Key, Guid.Parse(paymentAccountId));

            var payload = await targetCollection.FindAsync(filter);

            return new Result<PaymentAccountDocument>(await payload.SingleOrDefaultAsync());
        }

        public async Task<Result<Guid>> InsertOneAsync(PaymentAccount payload)
        {
            var document = new PaymentAccountDocument
            {
                Payload = payload
            };

            var targetCollection = await GetPaymentAccountsCollectionAsync();

            await targetCollection.InsertOneAsync(document);

            return new Result<Guid>(document.Payload.Key);
        }

        public async Task<Result<Guid>> RemoveAsync(string paymentAccountId)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var paymentAccountIdForDelete = Guid.Parse(paymentAccountId);

            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Payload.Key, paymentAccountIdForDelete);

            await targetCollection.DeleteOneAsync(filter);

            return new Result<Guid>(paymentAccountIdForDelete);
        }

        public async Task<Result<Guid>> UpdateAsync(string requestPaymentAccountGuid, PaymentAccount paymentAccountForUpdate)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var paymentAccountIdForUpdate = Guid.Parse(requestPaymentAccountGuid);

            var documentResult = await GetByIdAsync(requestPaymentAccountGuid);

            var replacement = new PaymentAccountDocument
            {
                Id = documentResult.Payload.Id,
                Payload = paymentAccountForUpdate
            };

            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Id, replacement.Id);

            await targetCollection.ReplaceOneAsync(filter, replacement);

            return new Result<Guid>(paymentAccountIdForUpdate);
        }

        private async Task<IMongoCollection<PaymentAccountDocument>> GetPaymentAccountsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<PaymentAccountDocument>("payment-accounts");

            var collectionIndexes = await collection.Indexes.ListAsync();

            if (await collectionIndexes.AnyAsync())
            {
                return collection;
            }

            var indexKeysDefinition = Builders<PaymentAccountDocument>.IndexKeys
                .Ascending(c => c.Payload.Key)
                .Ascending(c => c.Payload.Type);

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentAccountDocument>(indexKeysDefinition));

            return collection;
        }
    }
}
