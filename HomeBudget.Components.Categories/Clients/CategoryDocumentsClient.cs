using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Categories.Clients
{
    internal class CategoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
        : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.HandBooks),
        ICategoryDocumentsClient
    {
        private const string PayloadKeyIndexName = "ux_categories_payload_key";
        private const string CategoryKeyIndexName = "ux_categories_payload_category_key";

        public async Task<Result<IReadOnlyCollection<CategoryDocument>>> GetAsync()
        {
            var targetCollection = await GetCategoriesCollectionAsync();

            var payload = await targetCollection.FindAsync(_ => true);

            return Result<IReadOnlyCollection<CategoryDocument>>.Succeeded(await payload.ToListAsync());
        }

        public async Task<Result<CategoryDocument>> GetByIdAsync(Guid contractorId)
        {
            var targetCollection = await GetCategoriesCollectionAsync();

            var payload = await targetCollection.FindAsync(d => d.Payload.Key.CompareTo(contractorId) == 0);

            return Result<CategoryDocument>.Succeeded(await payload.SingleOrDefaultAsync());
        }

        public async Task<Result<Guid>> InsertOneAsync(Category payload)
        {
            var targetCollection = await GetCategoriesCollectionAsync();
            var filter = Builders<CategoryDocument>.Filter.Eq(d => d.Payload.CategoryKey, payload.CategoryKey);
            var now = DateTime.UtcNow;

            try
            {
                if (await targetCollection.Find(filter).AnyAsync())
                {
                    return Result<Guid>.Failure($"The category with '{payload.CategoryKey}' key already exists");
                }

                await targetCollection.InsertOneAsync(new CategoryDocument
                {
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

        public async Task<bool> CheckIfExistsAsync(string contractorKey)
        {
            var filter = Builders<CategoryDocument>.Filter.Eq(d => d.Payload.CategoryKey, contractorKey);

            var targetCollection = await GetCategoriesCollectionAsync();

            var payload = await targetCollection.FindAsync(filter);

            return await payload.AnyAsync();
        }

        public async Task<Result<IReadOnlyCollection<CategoryDocument>>> GetByIdsAsync(IEnumerable<Guid> operationIds)
        {
            var targetCollection = await GetCategoriesCollectionAsync();

            var payload = await targetCollection.FindAsync(d => operationIds.Contains(d.Payload.Key));

            var categories = await payload.ToListAsync();

            return Result<IReadOnlyCollection<CategoryDocument>>.Succeeded(categories);
        }

        private async Task<IMongoCollection<CategoryDocument>> GetCategoriesCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);

            await EnsureUniqueIndexAsync(collection, "Payload.Key", PayloadKeyIndexName);
            await EnsureUniqueIndexAsync(collection, "Payload.CategoryKey", CategoryKeyIndexName);

            return collection;
        }
    }
}
