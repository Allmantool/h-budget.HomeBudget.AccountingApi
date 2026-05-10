using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Accounts.Clients
{
    internal class PaymentAccountDocumentClient(IOptions<MongoDbOptions> dbOptions)
        : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.LedgerDatabase),
        IPaymentAccountDocumentClient
    {
        private const string PayloadKeyIndexName = "ux_payment_accounts_payload_key";
        private const string TypeIndexName = "ix_payment_accounts_payload_type";

        public async Task<Result<IReadOnlyCollection<PaymentAccountDocument>>> GetAsync()
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var payload = await targetCollection.FindAsync(_ => true);

            return Result<IReadOnlyCollection<PaymentAccountDocument>>.Succeeded(await payload.ToListAsync());
        }

        public async Task<Result<PaymentAccountDocument>> GetByIdAsync(string paymentAccountId)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Payload.Key, Guid.Parse(paymentAccountId));

            var payload = await targetCollection.FindAsync(filter);

            return Result<PaymentAccountDocument>.Succeeded(await payload.SingleOrDefaultAsync());
        }

        public async Task<Result<Guid>> InsertOneAsync(PaymentAccount payload)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();
            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Payload.Key, payload.Key);
            var now = DateTime.UtcNow;
            var update = Builders<PaymentAccountDocument>.Update
                .SetOnInsert(d => d.Payload, payload)
                .SetOnInsert(d => d.CreatedUtc, now)
                .SetOnInsert(d => d.UpdatedUtc, now);

            try
            {
                await targetCollection.UpdateOneAsync(
                    filter,
                    update,
                    new UpdateOptions { IsUpsert = true });

                return Result<Guid>.Succeeded(payload.Key);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await targetCollection.Find(filter).SingleOrDefaultAsync();
                if (existing != null)
                {
                    return Result<Guid>.Succeeded(existing.Payload.Key);
                }

                return Result<Guid>.Failure($"The payment account with '{payload.Key}' key already exists");
            }
        }

        public async Task<Result<Guid>> RemoveAsync(string paymentAccountId)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var paymentAccountIdForDelete = Guid.Parse(paymentAccountId);

            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Payload.Key, paymentAccountIdForDelete);

            await targetCollection.DeleteOneAsync(filter);

            return Result<Guid>.Succeeded(paymentAccountIdForDelete);
        }

        public async Task<Result<Guid>> UpdateAsync(string requestPaymentAccountGuid, PaymentAccount paymentAccountForUpdate)
        {
            var targetCollection = await GetPaymentAccountsCollectionAsync();

            var paymentAccountIdForUpdate = Guid.Parse(requestPaymentAccountGuid);

            var documentResult = await GetByIdAsync(requestPaymentAccountGuid);

            if (!documentResult.IsSucceeded || documentResult.Payload == null)
            {
                return Result<Guid>.Failure($"The payment account with '{requestPaymentAccountGuid}' hasn't been found");
            }

            var replacement = new PaymentAccountDocument
            {
                Id = documentResult.Payload.Id,
                Payload = paymentAccountForUpdate,
                SourceSystem = documentResult.Payload.SourceSystem,
                LegacyId = documentResult.Payload.LegacyId,
                ImportBatchId = documentResult.Payload.ImportBatchId,
                CreatedUtc = documentResult.Payload.CreatedUtc,
                UpdatedUtc = DateTime.UtcNow
            };

            var filter = Builders<PaymentAccountDocument>.Filter.Eq(d => d.Id, replacement.Id);

            await targetCollection.ReplaceOneAsync(filter, replacement);

            return Result<Guid>.Succeeded(paymentAccountIdForUpdate);
        }

        private async Task<IMongoCollection<PaymentAccountDocument>> GetPaymentAccountsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<PaymentAccountDocument>(LedgerDbCollections.PaymentAccounts);

            await EnsureUniqueIndexAsync(collection, "Payload.Key", PayloadKeyIndexName);
            await EnsureNonUniqueIndexAsync(collection, "Payload.Type", TypeIndexName);

            return collection;
        }
    }
}
