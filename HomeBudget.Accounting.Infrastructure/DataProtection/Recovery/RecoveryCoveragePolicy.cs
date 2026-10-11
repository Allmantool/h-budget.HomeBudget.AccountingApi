namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    internal static class RecoveryCoveragePolicy
    {
        public static RecoveryHistoryCoverage Required(RecoveryComponent component) => component switch
        {
            RecoveryComponent.Sql =>
                RecoveryHistoryCoverage.TransferRegistrations |
                RecoveryHistoryCoverage.ProcessingState,
            RecoveryComponent.EventStore => RecoveryHistoryCoverage.PaymentEvents,
            RecoveryComponent.Mongo =>
                RecoveryHistoryCoverage.Accounts |
                RecoveryHistoryCoverage.Categories |
                RecoveryHistoryCoverage.Contractors |
                RecoveryHistoryCoverage.ReferenceUpdatesAndDeletes |
                RecoveryHistoryCoverage.BusinessSettings |
                RecoveryHistoryCoverage.SettingsUpdatesAndDeletes |
                RecoveryHistoryCoverage.MigrationLineage,
            RecoveryComponent.Rates =>
                RecoveryHistoryCoverage.Rates |
                RecoveryHistoryCoverage.RateUpdatesAndDeletes,
            RecoveryComponent.PendingWork => RecoveryHistoryCoverage.AcceptedPendingCommands,
            _ => RecoveryHistoryCoverage.None
        };
    }
}
