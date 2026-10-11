using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal interface IRecoveryArtifactPipeline
    {
        Task<CapturedArtifactEvidence> CaptureAsync(
            RecoveryManifestDraft draft,
            CancellationToken cancellationToken);

        Task<IndependentCopyEvidence> CopyAsync(
            RecoveryManifestDraft draft,
            CapturedArtifactEvidence capture,
            CancellationToken cancellationToken);

        Task<IntegrityVerificationEvidence> VerifyAsync(
            RecoveryManifestDraft draft,
            CapturedArtifactEvidence capture,
            IndependentCopyEvidence copy,
            CancellationToken cancellationToken);
    }
}
