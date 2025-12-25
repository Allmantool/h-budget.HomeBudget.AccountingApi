namespace HomeBudget.Core.Constants
{
    public enum OutboxStatus
    {
        Pending = 0,
        Published = 1,
        Processed = 2,
        Failed = 3
    }
}
