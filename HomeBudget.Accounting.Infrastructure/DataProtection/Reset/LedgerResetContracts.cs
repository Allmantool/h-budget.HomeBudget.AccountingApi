#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Reset
{
    public enum LedgerResetStage
    {
        DisposePendingWork,
        IsolateMessaging,
        IsolateEventStore,
        IsolateMongo,
        IsolateSql,
        ClearCachesAndJobs,
        PrepareGeneration,
        VerifyPristine
    }

    public enum LedgerResetState
    {
        Running,
        Failed,
        Succeeded
    }

    public sealed record LedgerResetResource(
        string ResourceId,
        string ResourceKind,
        bool IsShared,
        bool PreserveUnrelatedScopes,
        string OwnershipProofSha256);

    public sealed record LedgerResetPlan(
        Guid OperationId,
        string EnvironmentId,
        string LedgerId,
        string CurrentGenerationId,
        string NewGenerationId,
        string HostResourceId,
        string StorageResourceId,
        DateTimeOffset CreatedUtc,
        IReadOnlyDictionary<string, long> AffectedEntityCounts,
        IReadOnlyList<LedgerResetResource> Resources,
        IReadOnlyList<string> PreservedPolicies,
        string PlanFingerprint);

    public sealed record LedgerResetRequest(LedgerResetPlan Plan);

    public sealed record LedgerResetContext(LedgerResetPlan Plan);

    public sealed record LedgerResetBackupEvidence(
        Guid RecoverySetId,
        bool IsSucceeded,
        bool IsIntegrityVerified,
        bool IsIndependentlyProtected,
        DateTimeOffset CommonCutoffUtc);

    public sealed record LedgerResetStageEvidence(
        LedgerResetStage Stage,
        Guid OperationId,
        string PlanFingerprint,
        string LedgerId,
        string CurrentGenerationId,
        string NewGenerationId,
        bool AffectsOnlySelectedLedger,
        bool OldWorkIsolated,
        bool RequiredResourcesReady,
        IReadOnlyDictionary<string, long> RemainingBusinessEntityCounts);

    public sealed record LedgerResetGenerationState(
        string ActiveGenerationId,
        string? PreviousGenerationId,
        Guid? ActivatedByOperationId,
        bool PreviousGenerationAvailable);

    public sealed record LedgerResetActivationEvidence(
        Guid OperationId,
        string PreviousGenerationId,
        string ActiveGenerationId,
        bool WasCompareAndSwapApplied,
        bool PreviousGenerationAvailable);

    public sealed record LedgerResetJournal(
        Guid OperationId,
        string PlanFingerprint,
        string EnvironmentId,
        string LedgerId,
        string CurrentGenerationId,
        string NewGenerationId,
        LedgerResetState State,
        long Revision,
        IReadOnlyList<LedgerResetStage> CompletedStages,
        LedgerResetBackupEvidence? PreBackup,
        LedgerResetActivationEvidence? Activation,
        bool RollbackEligible,
        string? ErrorCode);

    public sealed record LedgerResetResult(
        Guid OperationId,
        LedgerResetState State,
        string ActiveGenerationId,
        bool ReimportAllowed,
        bool RollbackEligible,
        IReadOnlyList<LedgerResetStage> CompletedStages);

    public interface ILedgerResetPreBackupGate
    {
        Task<LedgerResetBackupEvidence> RequireFreshBackupAsync(
            LedgerResetPlan plan,
            CancellationToken cancellationToken);
    }

    public interface ILedgerResetStageAdapter
    {
        LedgerResetStage Stage { get; }

        Task<LedgerResetStageEvidence> ExecuteAsync(
            LedgerResetContext context,
            CancellationToken cancellationToken);
    }

    public interface ILedgerResetGenerationGuard
    {
        Task<LedgerResetGenerationState> GetAsync(
            string environmentId,
            string ledgerId,
            CancellationToken cancellationToken);

        Task<LedgerResetActivationEvidence> TryActivateAsync(
            Guid operationId,
            string environmentId,
            string ledgerId,
            string expectedGenerationId,
            string newGenerationId,
            CancellationToken cancellationToken);
    }

    public interface ILedgerResetJournalStore
    {
        Task<LedgerResetJournal?> LoadAsync(Guid operationId, CancellationToken cancellationToken);

        Task<LedgerResetJournal> CreateAsync(
            LedgerResetJournal journal,
            CancellationToken cancellationToken);

        Task<LedgerResetJournal> RecordPreBackupAsync(
            Guid operationId,
            long expectedRevision,
            LedgerResetBackupEvidence evidence,
            CancellationToken cancellationToken);

        Task<LedgerResetJournal> RecordStageAsync(
            Guid operationId,
            long expectedRevision,
            LedgerResetStage stage,
            LedgerResetStageEvidence evidence,
            CancellationToken cancellationToken);

        Task<LedgerResetJournal> RecordActivationAsync(
            Guid operationId,
            long expectedRevision,
            LedgerResetActivationEvidence evidence,
            CancellationToken cancellationToken);

        Task MarkFailedAsync(
            Guid operationId,
            long expectedRevision,
            string errorCode,
            CancellationToken cancellationToken);
    }
}
