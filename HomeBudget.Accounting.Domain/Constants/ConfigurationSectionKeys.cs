namespace HomeBudget.Accounting.Domain.Constants
{
    public static class ConfigurationSectionKeys
    {
        public static readonly string KafkaOptions = nameof(KafkaOptions);
        public static readonly string PaymentsHistoryDbOptions = nameof(PaymentsHistoryDbOptions);
        public static readonly string EventStoreDb = nameof(EventStoreDb);
    }
}
