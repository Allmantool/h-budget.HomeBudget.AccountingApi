namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public enum DataProtectionOperationStartOutcome
    {
        Accepted,
        PlanNotFound,
        PlanExpired,
        ConfirmationConsumed,
        BindingMismatch
    }
}
