namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class DataProtectionOperationStartResult
    {
        public DataProtectionOperationStartOutcome Outcome { get; init; }
        public DataProtectionOperation Operation { get; init; }
    }
}
