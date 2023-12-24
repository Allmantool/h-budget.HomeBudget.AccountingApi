namespace HomeBudget.Accounting.Domain.Models
{
    public class ProducerSettings
    {
        public string BootstrapServers { get; set; }
        public string SecurityProtocol { get; set; }
        public string SslCertificatePem { get; set; }
        public string SslKeyPem { get; set; }
        public string SslCaLocation { get; set; }
    }
}
