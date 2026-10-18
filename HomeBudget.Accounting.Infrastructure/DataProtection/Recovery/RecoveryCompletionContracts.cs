using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryCompletionContext(
        Guid OperationId,
        RecoveryPlan Plan,
        RecoveryArtifactBinding Artifact,
        RecoveryExecutionTargetEvidence Target,
        bool SuppressNotifications,
        bool SuppressExternalSideEffects);

    public sealed record RecoveryCompletionBinding(
        Guid OperationId,
        Guid RecoverySetId,
        string PlanFingerprint,
        string ManifestSha256,
        string TargetId,
        string TargetGenerationId);

    public sealed record RecoveryPendingReplayEvidence(
        RecoveryCompletionBinding Binding,
        DateTimeOffset AcceptanceCutoffUtc,
        long AcceptedCommandCount,
        long SettledCommandCount,
        long DuplicateFinancialEffectCount,
        bool IdempotencyKeysPreserved,
        bool NotificationsSuppressed,
        bool ExternalSideEffectsSuppressed);

    public sealed record RecoveryProjectionRebuildEvidence(
        RecoveryCompletionBinding Binding,
        DateTimeOffset ProjectionCutoffUtc,
        bool DerivedStateRebuilt,
        bool CheckpointsRebuilt,
        bool AuthoritativeDataPreserved,
        bool NotificationsSuppressed,
        bool ExternalSideEffectsSuppressed);

    public sealed record RecoveryApiReconciliationEvidence(
        RecoveryCompletionBinding Binding,
        DateTimeOffset ReconciledCutoffUtc,
        bool Passed,
        bool IdentitiesAndCountsMatch,
        bool NoDuplicatesOrOrphans,
        bool ReferencesValid,
        bool PerAccountPeriodCurrencyTotalsMatch,
        bool BalancesMatch,
        bool OpeningBalancesAndAdjustmentsMatch,
        bool SplitsMatch,
        bool TransfersRatesAndFeesMatch,
        bool SettingsAndRequiredRatesMatch);

    public sealed record RecoveryCompletionStageEvidence(
        RecoveryExecutionStage Stage,
        RecoveryCompletionBinding Binding,
        string EvidenceSha256);

    public sealed record RecoveryActivationRequest(
        Guid OperationId,
        string EnvironmentId,
        string LedgerId,
        string ExpectedActiveGenerationId,
        string TargetGenerationId,
        string PlanFingerprint);

    public enum RecoveryActivationOutcome
    {
        Activated,
        AlreadyActivatedByOperation,
        Conflict
    }

    public sealed record RecoveryActivationEvidence(
        Guid OperationId,
        string EnvironmentId,
        string LedgerId,
        string PreviousGenerationId,
        string VisibleGenerationId,
        string PlanFingerprint,
        RecoveryActivationOutcome Outcome,
        bool IsAtomic);

    public sealed record RecoveryCompletionResult(
        Guid OperationId,
        RecoveryExecutionState State,
        long Revision,
        string VisibleGenerationId,
        bool Activated,
        bool RollbackEligible);

    public interface IRecoveryPendingReplayAdapter
    {
        Task<RecoveryPendingReplayEvidence> ReplayAsync(
            RecoveryCompletionContext context,
            CancellationToken cancellationToken);
    }

    public interface IRecoveryProjectionRebuildAdapter
    {
        Task<RecoveryProjectionRebuildEvidence> RebuildAsync(
            RecoveryCompletionContext context,
            CancellationToken cancellationToken);
    }

    public interface IRecoveryApiReconciliationAdapter
    {
        Task<RecoveryApiReconciliationEvidence> ReconcileAsync(
            RecoveryCompletionContext context,
            CancellationToken cancellationToken);
    }

    public interface IRecoveryGenerationActivator
    {
        Task<RecoveryActivationEvidence> CompareAndSwapAsync(
            RecoveryActivationRequest request,
            CancellationToken cancellationToken);
    }
}
