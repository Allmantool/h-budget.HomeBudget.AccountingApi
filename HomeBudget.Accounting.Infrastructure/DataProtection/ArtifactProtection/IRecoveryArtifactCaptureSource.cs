using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    public interface IRecoveryArtifactCaptureSource
    {
        Task<IReadOnlyCollection<PlaintextRecoveryArtifact>> CaptureAsync(
            RecoveryArtifactCaptureContext context,
            CancellationToken cancellationToken);
    }
}
