namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public enum DataProtectionOperationState
    {
        Queued,
        Validating,
        Running,
        AwaitingReconciliation,
        Succeeded,
        Failed,
        Cancelled
    }
}
