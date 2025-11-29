using System.Threading.Tasks;

using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class MongoDbContainerExtensions
    {
        public static async Task ResetContainersAsync(this MongoDbContainer container)
        {
            using var mongo = new MongoClient(container.GetConnectionString());
            var dbNames = await mongo.ListDatabaseNamesAsync();

            await foreach (var dbName in dbNames.ToAsyncEnumerable())
            {
                if (dbName is "admin" or "local" or "config")
                {
                    continue;
                }

                var db = mongo.GetDatabase(dbName);
                var collections = await db.ListCollectionNamesAsync();

                await foreach (var collection in collections.ToAsyncEnumerable())
                {
                    // Deletes all documents without dropping structure
                    await db.GetCollection<BsonDocument>(collection)
                            .DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
                }
            }
        }
    }
}
