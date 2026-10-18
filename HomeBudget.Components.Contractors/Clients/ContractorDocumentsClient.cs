using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Contractors.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Contractors.Clients
{
    internal class ContractorDocumentsClient(
        IOptions<MongoDbOptions> dbOptions,
        IActiveLedgerGenerationResolver activeGenerationResolver = null) :
        BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.HandBooks),
        IContractorDocumentsClient
    {
        private const string PayloadKeyIndexName = "ux_contractors_payload_key";
        private const string ContractorKeyIndexName = "ux_contractors_payload_contractor_key";
        private const string IdempotencyIndexName = "ux_contractors_idempotency_key_hash";

        public async Task<Result<IReadOnlyCollection<ContractorDocument>>> GetAsync()
        {
            var targetCollection = await GetContractorsCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var payload = await targetCollection.FindAsync(BuildScopeFilter(generation));

            return Result<IReadOnlyCollection<ContractorDocument>>.Succeeded(await payload.ToListAsync());
        }

        public async Task<Result<ContractorDocument>> GetByIdAsync(Guid contractorId)
        {
            var targetCollection = await GetContractorsCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var payload = await targetCollection.FindAsync(Builders<ContractorDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<ContractorDocument>.Filter.Eq(document => document.Payload.Key, contractorId)));

            return Result<ContractorDocument>.Succeeded(await payload.SingleOrDefaultAsync());
        }

        public async Task<bool> CheckIfExistsAsync(string contractorKey)
        {
            var targetCollection = await GetContractorsCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var filter = Builders<ContractorDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<ContractorDocument>.Filter.Eq(d => d.Payload.ContractorKey, contractorKey));

            var payload = await targetCollection.FindAsync(filter);

            return await payload.AnyAsync();
        }

        public async Task<Result<Guid>> InsertOneAsync(Contractor payload)
        {
            var targetCollection = await GetContractorsCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var filter = Builders<ContractorDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<ContractorDocument>.Filter.Eq(d => d.Payload.ContractorKey, payload.ContractorKey));
            var now = DateTime.UtcNow;

            try
            {
                if (await targetCollection.Find(filter).AnyAsync())
                {
                    return Result<Guid>.Failure($"The contractor with '{payload.ContractorKey}' key already exists");
                }

                await targetCollection.InsertOneAsync(new ContractorDocument
                {
                    LedgerId = generation.LedgerId,
                    LedgerGenerationId = generation.LedgerGenerationId,
                    Payload = payload,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });

                return Result<Guid>.Succeeded(payload.Key);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return Result<Guid>.Failure($"The contractor with '{payload.ContractorKey}' key already exists");
            }
        }

        public async Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
            Contractor payload,
            IdempotentDocumentWriteContext context,
            CancellationToken cancellationToken)
        {
            var collection = await GetContractorsCollectionAsync();
            var generation = await GetActiveGenerationAsync(cancellationToken);
            return await UpsertIdempotentAsync(
                collection,
                payload,
                static contractor => contractor.Key,
                context,
                generation.LedgerId,
                generation.LedgerGenerationId,
                cancellationToken);
        }

        private async Task<IMongoCollection<ContractorDocument>> GetContractorsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<ContractorDocument>(LedgerDbCollections.Contractors);
            await AdoptMissingLegacyScopeAsync(collection, await GetActiveGenerationAsync());

            await EnsureUniqueCompoundIndexAsync(collection, PayloadKeyIndexName, "LedgerId", "LedgerGenerationId", "Payload.Key");
            await EnsureUniqueCompoundIndexAsync(collection, ContractorKeyIndexName, "LedgerId", "LedgerGenerationId", "Payload.ContractorKey");
            await EnsureUniqueScopedPartialStringIndexAsync(collection, "IdempotencyKeyHash", IdempotencyIndexName);

            return collection;
        }

        private static FilterDefinition<ContractorDocument> BuildScopeFilter(LedgerGenerationScope generation)
        {
            return Builders<ContractorDocument>.Filter.And(
                Builders<ContractorDocument>.Filter.Eq(document => document.LedgerId, generation.LedgerId),
                Builders<ContractorDocument>.Filter.Eq(
                    document => document.LedgerGenerationId,
                    generation.LedgerGenerationId));
        }

        private Task<LedgerGenerationScope> GetActiveGenerationAsync(CancellationToken cancellationToken = default)
        {
            return activeGenerationResolver?.GetActiveAsync(cancellationToken)
                ?? Task.FromResult(LedgerGenerationScope.Legacy);
        }
    }
}
