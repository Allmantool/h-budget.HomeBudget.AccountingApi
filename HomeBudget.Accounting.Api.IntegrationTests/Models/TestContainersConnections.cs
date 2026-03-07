namespace HomeBudget.Accounting.Api.IntegrationTests.Models
{
    public record TestContainersConnections
    {
        public string EventSourceDbContainer { get; init; }
        public string KafkaContainer { get; init; }
        public string MongoDbContainer { get; init; }
        public string MsSqlDbContainer { get; init; }
    }
}
