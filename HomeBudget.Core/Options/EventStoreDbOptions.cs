using System;

namespace HomeBudget.Core.Options
{
    public record EventStoreDbOptions
    {
        public Uri Url { get; init; }

        public int TimeoutInSeconds { get; init; } = 90;
        public int GossipTimeout { get; init; } = 3;
        public int KeepAliveInterval { get; init; } = 10;
        public int DiscoveryInterval { get; init; } = 10;

        public int MaxDiscoverAttempts { get; init; } = 10;
        public int RetryAttempts { get; init; } = 5;

        public int EventBatchingDelayInMs { get; init; } = 100;

        public int MaxReconnectAttempts { get; init; } = 5;
        public int ChannelOperationTimeout { get; init; } = 120;
        public double RetryRiseNumber { get; init; } = 3;
        public int ChannelCapacity { get; init; } = 5000;

        public int EventProcessingBatchSize { get; init; } = 220;
        public int RequestRateLimiter { get; init; } = 32;

        public int BatchProcessingFlushPeriodInMs { get; init; } = 200;
    }
}