namespace HomeBudget.Accounting.Api.IntegrationTests.Models
{
    public class TestContainersConnections
    {
        public string EventSourceDbContainer { get; set; }
        public string KafkaContainer { get; set; }
        public string MongoDbContainer { get; set; }
    }
}
