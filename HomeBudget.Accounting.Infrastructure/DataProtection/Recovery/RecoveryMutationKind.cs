namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public enum RecoveryMutationKind
    {
        PaymentEvent,
        ReferenceUpdate,
        ReferenceDelete,
        SettingUpdate,
        SettingDelete,
        RateUpdate,
        RateDelete,
        AcceptedPendingCommand
    }
}
