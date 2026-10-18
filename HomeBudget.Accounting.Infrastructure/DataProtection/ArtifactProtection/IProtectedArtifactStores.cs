using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    public interface IProtectedArtifactStagingStore
    {
        Task<string> CreatePlaintextWorkspaceAsync(Guid recoverySetId, CancellationToken cancellationToken);

        Task<StagedRecoveryArtifact> WriteProtectedAsync(
            Guid recoverySetId,
            PlaintextRecoveryArtifact plaintext,
            Func<Stream, CancellationToken, Task<ArtifactEncryptionEvidence>> writeAsync,
            CancellationToken cancellationToken);

        Task<Stream> OpenProtectedReadAsync(string protectedSha256, CancellationToken cancellationToken);

        Task CleanupPlaintextAsync(Guid recoverySetId, CancellationToken cancellationToken);

        Task QuarantineAsync(Guid recoverySetId, string reasonCode, CancellationToken cancellationToken);
    }

    public interface IIndependentRecoveryArtifactStore
    {
        Task<ArtifactStoreCapability> ProbeAsync(CancellationToken cancellationToken);

        Task<IndependentArtifactEvidence> PutIfAbsentAsync(
            StagedRecoveryArtifact artifact,
            Stream protectedContent,
            CancellationToken cancellationToken);

        Task<Stream> OpenReadAsync(string protectedSha256, CancellationToken cancellationToken);

        Task<ManifestPublicationEvidence> PublishManifestIfAbsentAsync(
            Guid recoverySetId,
            ReadOnlyMemory<byte> manifest,
            string manifestSha256,
            CancellationToken cancellationToken);

        Task QuarantineAsync(Guid recoverySetId, string reasonCode, CancellationToken cancellationToken);
    }
}
