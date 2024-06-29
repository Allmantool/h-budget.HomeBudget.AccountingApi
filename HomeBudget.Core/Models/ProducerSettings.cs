namespace HomeBudget.Core.Models
{
    public class ProducerSettings
    {
        public string BootstrapServers { get; set; }
        public int? MessageTimeoutMs { get; set; }
        public string SecurityProtocol { get; set; }
        public string SslCertificatePem { get; set; }
        public string SslKeyPem { get; set; }
        public string SslCaLocation { get; set; }
    }
}
