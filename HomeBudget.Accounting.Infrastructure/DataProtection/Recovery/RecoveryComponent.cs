namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public enum RecoveryComponent
    {
        Sql,
        EventStore,
        Mongo,
        Rates,
        PendingWork
    }
}
