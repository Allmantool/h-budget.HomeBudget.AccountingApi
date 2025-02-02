namespace HomeBudget.Accounting.Api.IntegrationTests.Models
{
    public class TestContainersConnections
    {
        public string EventSourceDbContainer { get; init; }
        public string KafkaContainer { get; init; }
        public string MongoDbContainer { get; init; }
    }
}
