using System;

namespace HomeBudget.Core.Options
{
    public record EventStoreDbOptions
    {
        public Uri Url { get; init; }
        public int TimeoutInSeconds { get; init; } = 60;
        public int RetryAttempts { get; init; } = 3;
        public int EventBatchingDelayInMs { get; init; } = 100;
    }
}