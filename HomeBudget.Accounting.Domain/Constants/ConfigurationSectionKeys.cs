namespace HomeBudget.Accounting.Domain.Constants
{
    public static class ConfigurationSectionKeys
    {
        public static readonly string KafkaOptions = nameof(KafkaOptions);
        public static readonly string MongoDbOptions = nameof(MongoDbOptions);
        public static readonly string EventStoreDb = nameof(EventStoreDb);
        public static readonly string ElasticSearchOptions = nameof(ElasticSearchOptions);
        public static readonly string SeqOptions = nameof(SeqOptions);
    }
}
