using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Artifacts
{
    internal sealed class FailingIndependentArtifactStore : IIndependentRecoveryArtifactStore
    {
        private readonly IIndependentRecoveryArtifactStore _inner;

        public FailingIndependentArtifactStore(IIndependentRecoveryArtifactStore inner)
        {
            _inner = inner;
        }

        public Task<ArtifactStoreCapability> ProbeAsync(CancellationToken cancellationToken) =>
            _inner.ProbeAsync(cancellationToken);

        public Task<IndependentArtifactEvidence> PutIfAbsentAsync(
            StagedRecoveryArtifact artifact,
            Stream protectedContent,
            CancellationToken cancellationToken) =>
            throw new IOException("copy provider credential canary");

        public Task<Stream> OpenReadAsync(string protectedSha256, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(protectedSha256, cancellationToken);

        public Task<ManifestPublicationEvidence> PublishManifestIfAbsentAsync(
            Guid recoverySetId,
            ReadOnlyMemory<byte> manifest,
            string manifestSha256,
            CancellationToken cancellationToken) =>
            _inner.PublishManifestIfAbsentAsync(
                recoverySetId,
                manifest,
                manifestSha256,
                cancellationToken);

        public Task QuarantineAsync(
            Guid recoverySetId,
            string reasonCode,
            CancellationToken cancellationToken) =>
            _inner.QuarantineAsync(recoverySetId, reasonCode, cancellationToken);
    }
}
