using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Contractors.Models;

namespace HomeBudget.Components.Contractors.Clients
{
    internal class ContractorDocumentsClient(IOptions<PaymentsHistoryDbOptions> dbOptions) :
        BaseDocumentClient(dbOptions.Value.ConnectionString, dbOptions.Value.HandBooksDatabaseName),
        IContractorDocumentsClient
    {
        public async Task<Result<IReadOnlyCollection<ContractorDocument>>> GetAsync()
        {
            var targetCollection = await GetPaymentAccountCollectionAsync();

            var payload = await targetCollection.Find(_ => true).ToListAsync();

            return new Result<IReadOnlyCollection<ContractorDocument>>(payload);
        }

        public async Task<Result<ContractorDocument>> GetByIdAsync(Guid contractorId)
        {
            var targetCollection = await GetPaymentAccountCollectionAsync();

            var payload = await targetCollection.Find(d => d.Payload.Key.CompareTo(contractorId) == 0).SingleAsync();

            return new Result<ContractorDocument>(payload);
        }

        public async Task<bool> CheckIfExistsAsync(string contractorKey)
        {
            var targetCollection = await GetPaymentAccountCollectionAsync();

            return await targetCollection.Find(d => string.Equals(d.Payload.ContractorKey, contractorKey, StringComparison.OrdinalIgnoreCase)).AnyAsync();
        }

        public async Task<Result<string>> InsertOneAsync(Contractor payload)
        {
            var document = new ContractorDocument
            {
                Payload = payload
            };

            var targetCollection = await GetPaymentAccountCollectionAsync();

            await targetCollection.InsertOneAsync(document);

            return new Result<string>(document.Payload.ContractorKey);
        }

        private async Task<IMongoCollection<ContractorDocument>> GetPaymentAccountCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<ContractorDocument>("contractors");

            var collectionIndexes = await collection.Indexes.ListAsync();

            if (await collectionIndexes.AnyAsync())
            {
                return collection;
            }

            var indexKeysDefinition = Builders<ContractorDocument>.IndexKeys
                .Ascending(paymentsHistory => paymentsHistory.Payload.Key);

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<ContractorDocument>(indexKeysDefinition));

            return collection;
        }
    }
}
