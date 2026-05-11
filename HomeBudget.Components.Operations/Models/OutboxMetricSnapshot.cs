namespace HomeBudget.Components.Operations.Models
{
    internal sealed class OutboxMetricSnapshot
    {
        public long PendingCount { get; init; }
        public long FailedDeadLetterCount { get; init; }
        public int OldestPendingAgeSeconds { get; init; }
    }
}
