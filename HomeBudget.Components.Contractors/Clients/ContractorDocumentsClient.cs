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
        private const string PayloadKeyIndexName = "ux_contractors_payload_key";
        private const string ContractorKeyIndexName = "ux_contractors_payload_contractor_key";

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
            var targetCollection = await GetContractorsCollectionAsync();
            var filter = Builders<ContractorDocument>.Filter.Eq(d => d.Payload.ContractorKey, payload.ContractorKey);
            var now = DateTime.UtcNow;
            var update = Builders<ContractorDocument>.Update
                .SetOnInsert(d => d.Payload, payload)
                .SetOnInsert(d => d.CreatedUtc, now)
                .SetOnInsert(d => d.UpdatedUtc, now);

            try
            {
                var writeResult = await targetCollection.UpdateOneAsync(
                    filter,
                    update,
                    new UpdateOptions { IsUpsert = true });

                if (writeResult.UpsertedId != null)
                {
                    return Result<Guid>.Succeeded(payload.Key);
                }

                var existing = await targetCollection.Find(filter).SingleOrDefaultAsync();

                return Result<Guid>.Succeeded(existing.Payload.Key);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await targetCollection.Find(filter).SingleOrDefaultAsync();
                if (existing != null)
                {
                    return Result<Guid>.Succeeded(existing.Payload.Key);
                }

                return Result<Guid>.Failure($"The contractor with '{payload.ContractorKey}' key already exists");
            }
        }

        private async Task<IMongoCollection<ContractorDocument>> GetContractorsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<ContractorDocument>(LedgerDbCollections.Contractors);

            await EnsureUniqueIndexAsync(collection, "Payload.Key", PayloadKeyIndexName);
            await EnsureUniqueIndexAsync(collection, "Payload.ContractorKey", ContractorKeyIndexName);

            return collection;
        }
    }
}
