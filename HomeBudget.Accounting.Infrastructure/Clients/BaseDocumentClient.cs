using System;

using MongoDB.Driver;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class BaseDocumentClient : IDisposable
    {
        private readonly MongoClient _client;
        protected IMongoDatabase MongoDatabase { get; private set; }

        private bool _disposed;

        protected BaseDocumentClient(string connectionString, string databaseName)
        {
            _client = new MongoClient(connectionString);
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
