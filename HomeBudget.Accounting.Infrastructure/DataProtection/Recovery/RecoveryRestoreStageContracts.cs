using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryRestoreContext(
        Guid OperationId,
        RecoveryPlan Plan,
        RecoveryArtifactBinding Artifact,
        RecoveryExecutionTargetEvidence Target);

    public sealed record RecoveryStageCompletionEvidence(
        RecoveryExecutionStage Stage,
        Guid RecoverySetId,
        string PlanFingerprint,
        string ManifestSha256,
        string TargetId,
        string TargetGenerationId,
        bool IsChecksumVerified,
        bool IsVersionCompatible,
        bool IsTargetIdentityVerified);

    public interface IRecoveryRestoreStageAdapter
    {
        RecoveryExecutionStage Stage { get; }

        Task<RecoveryStageCompletionEvidence> ExecuteAsync(
            RecoveryRestoreContext context,
            CancellationToken cancellationToken);
    }
}
