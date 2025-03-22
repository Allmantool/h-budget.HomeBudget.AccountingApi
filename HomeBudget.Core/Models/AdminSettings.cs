namespace HomeBudget.Core.Models
{
    public record AdminSettings
    {
        public string BootstrapServers { get; set; }
        public int OperationTimeoutInSeconds { get; init; } = 90;
        public int RequestTimeoutInSeconds { get; init; } = 90;
        public int SocketTimeoutMs { get; init; } = 60000;
        public string Debug { get; init; } = "admin";
        public int NumPartitions { get; set; } = 1;
        public short ReplicationFactor { get; set; } = -1;
        public int CancellationDelayMaxMs { get; set; } = 200;
        public int? MetadataMaxAgeMs { get; set; } = 15000;
        public int? SocketConnectionSetupTimeoutMs { get; set; } = 30000;
        public int? RetryBackoffMs { get; set; } = 500;
    }
}
