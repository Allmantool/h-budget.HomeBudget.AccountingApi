namespace HomeBudget.Accounting.Domain.Models
{
    public class MongoDbOptions
    {
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string CollectionName { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
    }
}
