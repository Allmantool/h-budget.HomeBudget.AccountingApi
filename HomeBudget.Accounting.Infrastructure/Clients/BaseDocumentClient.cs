using MongoDB.Driver;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class BaseDocumentClient
    {
        protected IMongoDatabase MongoDatabase { get; private set; }

        protected BaseDocumentClient(string connectionString, string databaseName)
        {
            var client = new MongoClient(connectionString);

            MongoDatabase = client.GetDatabase(databaseName);
        }
    }
}
