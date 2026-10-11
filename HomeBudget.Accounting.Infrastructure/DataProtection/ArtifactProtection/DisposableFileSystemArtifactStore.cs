using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    internal sealed partial class DisposableFileSystemArtifactStore :
        IProtectedArtifactStagingStore,
        IIndependentRecoveryArtifactStore
    {
        public const string MarkerFileName = ".home-ledger-disposable-artifact-root";

        private readonly string _rootPath;
        private readonly string _expectedMarker;
        private readonly ArtifactStoreCapability _capability;

        public DisposableFileSystemArtifactStore(
            DisposableArtifactRootIdentity identity,
            ArtifactProtectionProfile profile,
            ArtifactStoreCapability capability)
        {
            if (profile == ArtifactProtectionProfile.Production)
            {
                throw Failure("filesystem-production-forbidden", "Filesystem artifact storage is test-only.");
            }

            _rootPath = ValidateRoot(identity);
            _expectedMarker = $"{identity.RootId}\n{identity.OwnershipToken}";
            _capability = ValidateCapability(capability);
        }

        public Task<string> CreatePlaintextWorkspaceAsync(Guid recoverySetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = PathFor("plaintext", recoverySetId.ToString("N"));
            Directory.CreateDirectory(path);
            return Task.FromResult(path);
        }

        public async Task<StagedRecoveryArtifact> WriteProtectedAsync(
            Guid recoverySetId,
            PlaintextRecoveryArtifact plaintext,
            Func<Stream, CancellationToken, Task<ArtifactEncryptionEvidence>> writeAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            ArgumentNullException.ThrowIfNull(writeAsync);
            ValidateLogicalName(plaintext.LogicalName);
            var workspace = PathFor("plaintext", recoverySetId.ToString("N"));
            var plaintextPath = ValidateContainedFile(plaintext.FilePath, workspace);
            var plaintextEvidence = await HashFileAsync(plaintextPath, cancellationToken).ConfigureAwait(false);
            var temporaryPath = CreateTemporaryPath("staging-temp");

            try
            {
                ArtifactEncryptionEvidence encryption;
                await using (var output = OpenNewWrite(temporaryPath))
                {
                    encryption = await writeAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var protectedEvidence = await HashFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                CommitContentAddressed(
                    temporaryPath,
                    ContentPath("staging", protectedEvidence.Sha256),
                    protectedEvidence);
                return new StagedRecoveryArtifact(
                    plaintext.LogicalName,
                    plaintextEvidence.Size,
                    plaintextEvidence.Sha256,
                    protectedEvidence.Size,
                    protectedEvidence.Sha256,
                    encryption,
                    plaintext.Dependencies ?? Array.Empty<string>());
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }

        public Task<Stream> OpenProtectedReadAsync(string protectedSha256, CancellationToken cancellationToken) =>
            OpenReadAsyncCore(ContentPath("staging", ValidateSha256(protectedSha256)), cancellationToken);

        public Task CleanupPlaintextAsync(Guid recoverySetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureDeleteDirectory(PathFor("plaintext", recoverySetId.ToString("N")));
            return Task.CompletedTask;
        }

        public Task QuarantineAsync(Guid recoverySetId, string reasonCode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteQuarantineMarker(recoverySetId, reasonCode);
            return Task.CompletedTask;
        }

        public Task<ArtifactStoreCapability> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateMarker(_rootPath, _expectedMarker);
            return Task.FromResult(_capability);
        }

        public async Task<IndependentArtifactEvidence> PutIfAbsentAsync(
            StagedRecoveryArtifact artifact,
            Stream protectedContent,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            ArgumentNullException.ThrowIfNull(protectedContent);
            var expectedHash = ValidateSha256(artifact.ProtectedSha256);
            var temporaryPath = CreateTemporaryPath("object-temp");
            try
            {
                await using (var output = OpenNewWrite(temporaryPath))
                {
                    await protectedContent.CopyToAsync(output, 64 * 1024, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var actual = await HashFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                if (actual.Size != artifact.ProtectedSize ||
                    !string.Equals(actual.Sha256, expectedHash, StringComparison.Ordinal))
                {
                    throw Failure("copy-integrity-mismatch", "Protected artifact copy integrity failed.");
                }

                CommitContentAddressed(temporaryPath, ContentPath("objects", expectedHash), actual);
                return new IndependentArtifactEvidence(
                    artifact.LogicalName,
                    expectedHash,
                    actual.Size,
                    _capability.ImmutabilityState == ArtifactImmutabilityState.Immutable,
                    _capability.ImmutabilityState == ArtifactImmutabilityState.Offline);
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }

        public Task<Stream> OpenReadAsync(string protectedSha256, CancellationToken cancellationToken) =>
            OpenReadAsyncCore(ContentPath("objects", ValidateSha256(protectedSha256)), cancellationToken);

        public async Task<ManifestPublicationEvidence> PublishManifestIfAbsentAsync(
            Guid recoverySetId,
            ReadOnlyMemory<byte> manifest,
            string manifestSha256,
            CancellationToken cancellationToken)
        {
            var expectedHash = ValidateSha256(manifestSha256);
            if (!string.Equals(Sha256(manifest.Span), expectedHash, StringComparison.Ordinal))
            {
                throw Failure("manifest-integrity-mismatch", "Recovery manifest integrity failed.");
            }

            var temporaryPath = CreateTemporaryPath("manifest-temp");
            var destination = PathFor("manifests", $"{recoverySetId:N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, manifest.ToArray(), cancellationToken).ConfigureAwait(false);
                CommitManifest(temporaryPath, destination, expectedHash);
                return new ManifestPublicationEvidence(recoverySetId, expectedHash, true, true);
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }

        Task IIndependentRecoveryArtifactStore.QuarantineAsync(
            Guid recoverySetId,
            string reasonCode,
            CancellationToken cancellationToken) => QuarantineAsync(recoverySetId, reasonCode, cancellationToken);

        private static ArtifactProtectionException Failure(string code, string message) => new(code, message);
    }
}
