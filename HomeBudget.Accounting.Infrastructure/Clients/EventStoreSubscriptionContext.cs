namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public sealed class EventStoreSubscriptionContext
    {
        public string StreamId { get; init; }
        public string Revision { get; init; }
        public string Position { get; init; }
    }
}

