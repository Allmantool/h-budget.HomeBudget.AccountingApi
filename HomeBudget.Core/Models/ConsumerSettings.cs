namespace HomeBudget.Core.Models
{
    public record ConsumerSettings
    {
        public string BootstrapServers { get; set; }
        public string ClientId { get; set; }
        public string GroupId { get; set; }
        public bool EnableAutoCommit { get; set; }
        public int? FetchMaxBytes { get; set; } = 1048576;
        public int FetchWaitMaxMs { get; set; } = 5;
        public int AutoOffsetReset { get; set; } = 1;
        public bool AllowAutoCreateTopics { get; set; }
        public int MaxPollIntervalMs { get; set; } = 600000;
        public int SessionTimeoutMs { get; set; } = 90000;
        public int HeartbeatIntervalMs { get; set; } = 3000;
        public int ConsumerCircuitBreakerDelayInSeconds { get; set; } = 5;
        public long ConsumeDelayInMilliseconds { get; set; } = 800;
        public string Debug { get; set; } = "all";
        public int PartitionAssignmentStrategy { get; set; } = 2;
    }
}
