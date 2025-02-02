namespace HomeBudget.Core.Models
{
    public class ConsumerSettings
    {
        public string BootstrapServers { get; set; }
        public string ClientId { get; set; }
        public string GroupId { get; set; }
        public bool EnableAutoCommit { get; set; }
        public long FetchMaxBytes { get; set; }
        public int SessionTimeoutMs { get; set; }
    }
}
