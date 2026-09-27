using System;
using System.Collections.Generic;
using System.Threading;
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
        private const string IdempotencyIndexName = "ux_payment_accounts_idempotency_key_hash";

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

        public async Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
            PaymentAccount payload,
            IdempotentDocumentWriteContext context,
            CancellationToken cancellationToken)
        {
            var collection = await GetPaymentAccountsCollectionAsync();
            return await UpsertIdempotentAsync(
                collection,
                payload,
                static account => account.Key,
                context,
                cancellationToken);
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
            var filter = Builders<PaymentAccountDocument>.Filter.Eq(
                document => document.Payload.Key,
                paymentAccountIdForUpdate);
            var update = Builders<PaymentAccountDocument>.Update
                .Set(document => document.Payload.Agent, paymentAccountForUpdate.Agent)
                .Set(document => document.Payload.Currency, paymentAccountForUpdate.Currency)
                .Set(document => document.Payload.Description, paymentAccountForUpdate.Description)
                .Set(document => document.Payload.Type, paymentAccountForUpdate.Type)
                .Set(document => document.UpdatedUtc, DateTime.UtcNow);
            var result = await targetCollection.UpdateOneAsync(filter, update);

            return result.MatchedCount == 1
                ? Result<Guid>.Succeeded(paymentAccountIdForUpdate)
                : Result<Guid>.Failure($"The payment account with '{requestPaymentAccountGuid}' hasn't been found");
        }

        public async Task<Result<Guid>> UpdateBalanceIfNewerAsync(
            Guid paymentAccountId,
            decimal balance,
            long projectionFence,
            CancellationToken cancellationToken)
        {
            if (projectionFence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(projectionFence));
            }

            var collection = await GetPaymentAccountsCollectionAsync();
            var filter = Builders<PaymentAccountDocument>.Filter.And(
                Builders<PaymentAccountDocument>.Filter.Eq(document => document.Payload.Key, paymentAccountId),
                Builders<PaymentAccountDocument>.Filter.Or(
                    Builders<PaymentAccountDocument>.Filter.Exists(
                        document => document.PaymentHistoryProjectionFence,
                        exists: false),
                    Builders<PaymentAccountDocument>.Filter.Lt(
                        document => document.PaymentHistoryProjectionFence,
                        projectionFence)));
            var update = Builders<PaymentAccountDocument>.Update
                .Set(document => document.Payload.Balance, balance)
                .Set(document => document.PaymentHistoryProjectionFence, projectionFence)
                .Set(document => document.UpdatedUtc, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            if (result.MatchedCount == 1)
            {
                return Result<Guid>.Succeeded(paymentAccountId);
            }

            var existing = await collection
                .Find(document => document.Payload.Key == paymentAccountId)
                .SingleOrDefaultAsync(cancellationToken);
            return existing is not null && existing.PaymentHistoryProjectionFence >= projectionFence
                ? Result<Guid>.Succeeded(paymentAccountId)
                : Result<Guid>.Failure($"The payment account with '{paymentAccountId}' hasn't been found");
        }

        public async Task<Result<Guid>> UpdateBalanceAsync(
            Guid paymentAccountId,
            decimal balance,
            CancellationToken cancellationToken)
        {
            var collection = await GetPaymentAccountsCollectionAsync();
            var filter = Builders<PaymentAccountDocument>.Filter.Eq(
                document => document.Payload.Key,
                paymentAccountId);
            var update = Builders<PaymentAccountDocument>.Update
                .Set(document => document.Payload.Balance, balance)
                .Set(document => document.UpdatedUtc, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(
                filter,
                update,
                cancellationToken: cancellationToken);

            return result.MatchedCount == 1
                ? Result<Guid>.Succeeded(paymentAccountId)
                : Result<Guid>.Failure($"The payment account with '{paymentAccountId}' hasn't been found");
        }

        private async Task<IMongoCollection<PaymentAccountDocument>> GetPaymentAccountsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<PaymentAccountDocument>(LedgerDbCollections.PaymentAccounts);

            await EnsureUniqueIndexAsync(collection, "Payload.Key", PayloadKeyIndexName);
            await EnsureNonUniqueIndexAsync(collection, "Payload.Type", TypeIndexName);
            await EnsureUniquePartialStringIndexAsync(collection, "IdempotencyKeyHash", IdempotencyIndexName);

            return collection;
        }
    }
}
