namespace HomeBudget.Accounting.Infrastructure.Constants
{
    public enum KafkaMessageProcessingOutcome
    {
        Processed,
        Persisted,
        Duplicate,
        DeadLetter,
        Ignored
    }
}
