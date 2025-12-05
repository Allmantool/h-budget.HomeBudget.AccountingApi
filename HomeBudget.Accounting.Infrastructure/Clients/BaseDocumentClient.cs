using System;

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
    }
}
