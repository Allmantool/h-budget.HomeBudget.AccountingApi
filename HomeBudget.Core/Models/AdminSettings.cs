namespace HomeBudget.Core.Models
{
    public record AdminSettings
    {
        public string BootstrapServers { get; set; }
        public int OperationTimeoutInSeconds { get; init; } = 60;
        public int RequestTimeoutInSeconds { get; init; } = 60;
        public int SocketTimeoutMs { get; init; } = 60000;
        public string Debug { get; init; } = "all";
        public int NumPartitions { get; set; } = 3;
        public short ReplicationFactor { get; set; } = 1;
    }
}
