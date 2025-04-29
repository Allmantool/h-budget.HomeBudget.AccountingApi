using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Contractors.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Contractors.Clients
{
    internal class ContractorDocumentsClient(IOptions<MongoDbOptions> dbOptions) :
        BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.HandBooks),
        IContractorDocumentsClient
    {
        public async Task<Result<IReadOnlyCollection<ContractorDocument>>> GetAsync()
        {
            var targetCollection = await GetContractorsCollectionAsync();

            var payload = await targetCollection.FindAsync(_ => true);

            return Result<IReadOnlyCollection<ContractorDocument>>.Succeeded(await payload.ToListAsync());
        }

        public async Task<Result<ContractorDocument>> GetByIdAsync(Guid contractorId)
        {
            var targetCollection = await GetContractorsCollectionAsync();

            var payload = await targetCollection.FindAsync(d => d.Payload.Key.CompareTo(contractorId) == 0);

            return Result<ContractorDocument>.Succeeded(await payload.SingleOrDefaultAsync());
        }

        public async Task<bool> CheckIfExistsAsync(string contractorKey)
        {
            var filter = Builders<ContractorDocument>.Filter.Eq(d => d.Payload.ContractorKey, contractorKey);

            var targetCollection = await GetContractorsCollectionAsync();

            var payload = await targetCollection.FindAsync(filter);

            return await payload.AnyAsync();
        }

        public async Task<Result<Guid>> InsertOneAsync(Contractor payload)
        {
            var document = new ContractorDocument
            {
                Payload = payload
            };

            var targetCollection = await GetContractorsCollectionAsync();

            await targetCollection.InsertOneAsync(document);

            return Result<Guid>.Succeeded(document.Payload.Key);
        }

        private async Task<IMongoCollection<ContractorDocument>> GetContractorsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<ContractorDocument>(LedgerDbCollections.Contractors);

            var collectionIndexes = await collection.Indexes.ListAsync();

            if (await collectionIndexes.AnyAsync())
            {
                return collection;
            }

            var indexKeysDefinition = Builders<ContractorDocument>.IndexKeys
                .Ascending(c => c.Payload.Key)
                .Ascending(c => c.Payload.ContractorKey);

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<ContractorDocument>(indexKeysDefinition));

            return collection;
        }
    }
}
