namespace HomeBudget.Core.Models
{
    public record ConsumerSettings
    {
        public string BootstrapServers { get; set; }
        public string ClientId { get; set; }
        public string GroupId { get; set; }
        public bool EnableAutoCommit { get; set; }
        public long FetchMaxBytes { get; set; }
        public int AutoOffsetReset { get; set; } = 1;
        public bool AllowAutoCreateTopics { get; set; }
        public int MaxPollIntervalMs { get; set; } = 300000;
        public int SessionTimeoutMs { get; set; } = 45000;
        public int HeartbeatIntervalMs { get; set; } = 15000;
        public int ConsumerCircuitBreakerDelayInSeconds { get; set; } = 5;
        public int ConsumeDelayInSeconds { get; set; } = 1;
    }
}
