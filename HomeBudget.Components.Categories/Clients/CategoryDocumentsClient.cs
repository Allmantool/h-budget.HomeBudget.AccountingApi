using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;

namespace HomeBudget.Components.Categories.Clients
{
    internal class CategoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
        : BaseDocumentClient(dbOptions.Value.ConnectionString, dbOptions.Value.HandBooksDatabaseName),
        ICategoryDocumentsClient
    {
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
            var document = new CategoryDocument
            {
                Payload = payload
            };

            var targetCollection = await GetCategoriesCollectionAsync();

            await targetCollection.InsertOneAsync(document);

            return Result<Guid>.Succeeded(document.Payload.Key);
        }

        public async Task<bool> CheckIfExistsAsync(string contractorKey)
        {
            var filter = Builders<CategoryDocument>.Filter.Eq(d => d.Payload.CategoryKey, contractorKey);

            var targetCollection = await GetCategoriesCollectionAsync();

            var payload = await targetCollection.FindAsync(filter);

            return await payload.AnyAsync();
        }

        private async Task<IMongoCollection<CategoryDocument>> GetCategoriesCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<CategoryDocument>("categories");

            var collectionIndexes = await collection.Indexes.ListAsync();

            if (await collectionIndexes.AnyAsync())
            {
                return collection;
            }

            var indexKeysDefinition = Builders<CategoryDocument>.IndexKeys
                .Ascending(c => c.Payload.Key)
                .Ascending(c => c.Payload.CategoryKey);

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<CategoryDocument>(indexKeysDefinition));

            return collection;
        }
    }
}
