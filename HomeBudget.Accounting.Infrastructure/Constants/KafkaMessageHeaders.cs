namespace HomeBudget.Accounting.Infrastructure.Constants
{
    public static class KafkaMessageHeaders
    {
        public static readonly string Type = "Message-Type";
        public static readonly string Version = "Message-Version";
        public static readonly string Source = "Source";
        public static readonly string EnvelopId = "Envelop-Id";
        public static readonly string OccuredOn = "Occured-On";
        public static readonly string ProcessedAt = "Processed-At";
    }
}
