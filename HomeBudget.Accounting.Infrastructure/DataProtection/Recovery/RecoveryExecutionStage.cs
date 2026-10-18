namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public enum RecoveryExecutionStage
    {
        PrepareTarget,
        RestoreSql,
        RestoreEventStore,
        RestoreMongo,
        RestoreRates,
        ReconstructMessaging,
        ReconstructProcessingState,
        ReplayAcceptedPending,
        RebuildProjections,
        ReconcileApis
    }

    public enum RecoveryExecutionState
    {
        Running,
        Failed,
        AwaitingReplayRebuild,
        ReadyToActivate,
        Activated
    }
}
