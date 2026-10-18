using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Categories.Clients
{
    internal class CategoryDocumentsClient(
        IOptions<MongoDbOptions> dbOptions,
        IActiveLedgerGenerationResolver activeGenerationResolver = null)
        : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.HandBooks),
        ICategoryDocumentsClient
    {
        private const string PayloadKeyIndexName = "ux_categories_payload_key";
        private const string CategoryKeyIndexName = "ux_categories_payload_category_key";
        private const string IdempotencyIndexName = "ux_categories_idempotency_key_hash";

        public async Task<Result<IReadOnlyCollection<CategoryDocument>>> GetAsync()
        {
            var targetCollection = await GetCategoriesCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var payload = await targetCollection.FindAsync(BuildScopeFilter(generation));

            return Result<IReadOnlyCollection<CategoryDocument>>.Succeeded(await payload.ToListAsync());
        }

        public async Task<Result<CategoryDocument>> GetByIdAsync(Guid contractorId)
        {
            var targetCollection = await GetCategoriesCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var payload = await targetCollection.FindAsync(Builders<CategoryDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<CategoryDocument>.Filter.Eq(document => document.Payload.Key, contractorId)));

            return Result<CategoryDocument>.Succeeded(await payload.SingleOrDefaultAsync());
        }

        public async Task<Result<Guid>> InsertOneAsync(Category payload)
        {
            var targetCollection = await GetCategoriesCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var filter = Builders<CategoryDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<CategoryDocument>.Filter.Eq(d => d.Payload.CategoryKey, payload.CategoryKey));
            var now = DateTime.UtcNow;

            try
            {
                if (await targetCollection.Find(filter).AnyAsync())
                {
                    return Result<Guid>.Failure($"The category with '{payload.CategoryKey}' key already exists");
                }

                await targetCollection.InsertOneAsync(new CategoryDocument
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
                return Result<Guid>.Failure($"The category with '{payload.CategoryKey}' key already exists");
            }
        }

        public async Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
            Category payload,
            IdempotentDocumentWriteContext context,
            CancellationToken cancellationToken)
        {
            var collection = await GetCategoriesCollectionAsync();
            var generation = await GetActiveGenerationAsync(cancellationToken);
            return await UpsertIdempotentAsync(
                collection,
                payload,
                static category => category.Key,
                context,
                generation.LedgerId,
                generation.LedgerGenerationId,
                cancellationToken);
        }

        public async Task<bool> CheckIfExistsAsync(string contractorKey)
        {
            var targetCollection = await GetCategoriesCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var filter = Builders<CategoryDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<CategoryDocument>.Filter.Eq(d => d.Payload.CategoryKey, contractorKey));

            var payload = await targetCollection.FindAsync(filter);

            return await payload.AnyAsync();
        }

        public async Task<Result<IReadOnlyCollection<CategoryDocument>>> GetByIdsAsync(IEnumerable<Guid> operationIds)
        {
            var targetCollection = await GetCategoriesCollectionAsync();
            var generation = await GetActiveGenerationAsync();
            var payload = await targetCollection.FindAsync(Builders<CategoryDocument>.Filter.And(
                BuildScopeFilter(generation),
                Builders<CategoryDocument>.Filter.In(document => document.Payload.Key, operationIds)));

            var categories = await payload.ToListAsync();

            return Result<IReadOnlyCollection<CategoryDocument>>.Succeeded(categories);
        }

        private async Task<IMongoCollection<CategoryDocument>> GetCategoriesCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            await AdoptMissingLegacyScopeAsync(collection, await GetActiveGenerationAsync());

            await EnsureUniqueCompoundIndexAsync(collection, PayloadKeyIndexName, "LedgerId", "LedgerGenerationId", "Payload.Key");
            await EnsureUniqueCompoundIndexAsync(collection, CategoryKeyIndexName, "LedgerId", "LedgerGenerationId", "Payload.CategoryKey");
            await EnsureUniqueScopedPartialStringIndexAsync(collection, "IdempotencyKeyHash", IdempotencyIndexName);

            return collection;
        }

        private static FilterDefinition<CategoryDocument> BuildScopeFilter(LedgerGenerationScope generation)
        {
            return Builders<CategoryDocument>.Filter.And(
                Builders<CategoryDocument>.Filter.Eq(document => document.LedgerId, generation.LedgerId),
                Builders<CategoryDocument>.Filter.Eq(
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
