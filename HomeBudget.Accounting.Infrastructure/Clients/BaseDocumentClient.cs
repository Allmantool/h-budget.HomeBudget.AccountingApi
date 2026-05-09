using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using MongoDB.Bson;
using MongoDB.Driver;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class BaseDocumentClient : IDisposable
    {
        private readonly MongoClient _client;
        protected IMongoDatabase MongoDatabase { get; private set; }

        private bool _disposed;

        protected BaseDocumentClient(MongoDbOptions options, string database)
        {
            if (options == null)
            {
                return;
            }

            var databaseName = string.IsNullOrWhiteSpace(database)
                ? options.LedgerDatabase
                : database;

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return;
            }

            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.MaxConnectionPoolSize = options.MaxConnectionPoolSize;
            settings.MinConnectionPoolSize = options.MinConnectionPoolSize;
            settings.ConnectTimeout = TimeSpan.FromMinutes(options.ConnectTimeoutInMinutes);
            settings.HeartbeatTimeout = TimeSpan.FromSeconds(options.HeartbeatTimeoutInSeconds);
            settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(options.MaxConnectionIdleTimeInMinutes);
            settings.MaxConnectionLifeTime = TimeSpan.FromMinutes(options.MaxConnectionLifeTimeInMinutes);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(options.ServerSelectionTimeoutInSeconds);
            settings.SocketTimeout = TimeSpan.FromSeconds(options.SocketTimeoutInSeconds);
            settings.WaitQueueTimeout = TimeSpan.FromSeconds(options.WaitQueueTimeoutInSeconds);

            _client = new MongoClient(settings);

            MongoDatabase = _client.GetDatabase(databaseName);
        }

        protected static async Task EnsureUniqueIndexAsync<TDocument>(
            IMongoCollection<TDocument> collection,
            string fieldPath,
            string indexName)
        {
            ArgumentNullException.ThrowIfNull(collection);

            var existingIndexes = await collection.Indexes.List().ToListAsync();
            if (HasUniqueIndex(existingIndexes, fieldPath))
            {
                return;
            }

            await EnsureNoDuplicateValuesAsync(collection, fieldPath, indexName);

            foreach (var index in FindNonUniqueEquivalentIndexes(existingIndexes, fieldPath))
            {
                await collection.Indexes.DropOneAsync(index);
            }

            var indexKeysDefinition = Builders<TDocument>.IndexKeys.Ascending(fieldPath);
            var indexOptions = new CreateIndexOptions
            {
                Name = indexName,
                Unique = true
            };

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<TDocument>(indexKeysDefinition, indexOptions));
        }

        protected static async Task EnsureNonUniqueIndexAsync<TDocument>(
            IMongoCollection<TDocument> collection,
            string fieldPath,
            string indexName)
        {
            ArgumentNullException.ThrowIfNull(collection);

            var existingIndexes = await collection.Indexes.List().ToListAsync();
            if (existingIndexes.Any(index => HasName(index, indexName) || HasSingleFieldKey(index, fieldPath)))
            {
                return;
            }

            var indexKeysDefinition = Builders<TDocument>.IndexKeys.Ascending(fieldPath);
            var indexOptions = new CreateIndexOptions
            {
                Name = indexName
            };

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<TDocument>(indexKeysDefinition, indexOptions));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Dispose managed resources here
                // Note: MongoClient does not have a Dispose method. Cleanup is unnecessary in this case.
                // Add any other cleanup code for managed resources here if needed.
            }

            _client.Dispose(true);

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private static async Task EnsureNoDuplicateValuesAsync<TDocument>(
            IMongoCollection<TDocument> collection,
            string fieldPath,
            string indexName)
        {
            var pipeline = new[]
            {
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$" + fieldPath },
                    { "count", new BsonDocument("$sum", 1) },
                    { "ids", new BsonDocument("$push", "$_id") }
                }),
                new BsonDocument("$match", new BsonDocument("count", new BsonDocument("$gt", 1))),
                new BsonDocument("$limit", 5)
            };

            var duplicates = await collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
            if (duplicates.Count == 0)
            {
                return;
            }

            var duplicateDescriptions = duplicates.Select(DescribeDuplicate);
            throw new MongoDuplicateKeyDiagnosticException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Cannot create unique Mongo index '{0}' on collection '{1}' for field '{2}' because duplicate values already exist: {3}. Clean or merge these documents before enabling the unique index.",
                    indexName,
                    collection.CollectionNamespace.CollectionName,
                    fieldPath,
                    string.Join("; ", duplicateDescriptions)));
        }

        private static bool HasUniqueIndex(IEnumerable<BsonDocument> indexes, string fieldPath)
        {
            return indexes.Any(index => HasSingleFieldKey(index, fieldPath) && index.GetValue("unique", false).ToBoolean());
        }

        private static IEnumerable<string> FindNonUniqueEquivalentIndexes(IEnumerable<BsonDocument> indexes, string fieldPath)
        {
            return indexes
                .Where(index => HasSingleFieldKey(index, fieldPath) && !index.GetValue("unique", false).ToBoolean())
                .Select(index => index.GetValue("name").AsString)
                .Where(name => name != "_id_");
        }

        private static bool HasSingleFieldKey(BsonDocument index, string fieldPath)
        {
            if (!index.TryGetValue("key", out var keyValue) || !keyValue.IsBsonDocument)
            {
                return false;
            }

            var key = keyValue.AsBsonDocument;

            return key.ElementCount == 1 &&
                key.TryGetValue(fieldPath, out var direction) &&
                direction.ToInt32() == 1;
        }

        private static bool HasName(BsonDocument index, string indexName)
        {
            return index.TryGetValue("name", out var name) && name.AsString == indexName;
        }

        private static string DescribeDuplicate(BsonDocument duplicate)
        {
            var value = duplicate.GetValue("_id", BsonNull.Value).ToString();
            var count = duplicate.GetValue("count", 0).ToInt32();
            var ids = duplicate.GetValue("ids", new BsonArray()).AsBsonArray.Select(id => id.ToString());

            return string.Format(
                CultureInfo.InvariantCulture,
                "value '{0}' appears {1} times in documents [{2}]",
                value,
                count,
                string.Join(", ", ids));
        }
    }
}
