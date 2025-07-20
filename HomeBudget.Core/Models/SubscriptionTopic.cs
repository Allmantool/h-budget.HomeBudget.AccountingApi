namespace HomeBudget.Core.Models
{
    public record SubscriptionTopic
    {
        public string Title { get; init; }
        public string ConsumerType { get; init; }
        public int ConsumersAmount { get; init; } = 1;
    }
}
