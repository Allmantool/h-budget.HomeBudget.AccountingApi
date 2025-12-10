namespace HomeBudget.Core.Models
{
    public class ProducerSettings
    {
        public string BootstrapServers { get; set; }
        public int MessageTimeoutMs { get; set; } = 160_000;
        public string SecurityProtocol { get; set; }
        public string SslCertificatePem { get; set; }
        public string SslKeyPem { get; set; }
        public string SslCaLocation { get; set; }
        public int QueueBufferingMaxKbytes { get; set; } = 1_024_000;
        public int QueueBufferingMaxMessages { get; set; } = 500_000;
        public int BatchSize { get; set; } = 512_000;
        public int LingerMs { get; set; } = 50;
        public int SocketTimeoutMs { get; set; } = 120_000;
        public int SocketSendBufferBytes { get; set; } = 1_048_576;
        public int SocketReceiveBufferBytes { get; set; } = 1_048_576;
        public int RequestTimeoutMs { get; set; } = 60_000;
        public int RetryBackoffMs { get; set; } = 100;
        public string CompressionType { get; set; } = "snappy";
        public int MaxInFlight { get; set; } = 5;
        public int? StatisticsIntervalMs { get; set; } = 5_000;
    }
}
