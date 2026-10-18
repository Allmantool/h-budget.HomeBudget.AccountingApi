using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    internal sealed partial class RecoveryArtifactPipeline : IRecoveryArtifactPipeline
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ArtifactProtectionPolicy _policy;
        private readonly IRecoveryArtifactCaptureSource[] _sources;
        private readonly IRecoveryArtifactEncryptor _encryptor;
        private readonly IProtectedArtifactStagingStore _stagingStore;
        private readonly IIndependentRecoveryArtifactStore _independentStore;

        public RecoveryArtifactPipeline(
            ArtifactProtectionPolicy policy,
            IEnumerable<IRecoveryArtifactCaptureSource> sources,
            IRecoveryArtifactEncryptor encryptor,
            IProtectedArtifactStagingStore stagingStore,
            IIndependentRecoveryArtifactStore independentStore)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(encryptor);
            ArgumentNullException.ThrowIfNull(stagingStore);
            ArgumentNullException.ThrowIfNull(independentStore);
            ValidatePolicy(policy);
            _policy = policy;
            _sources = sources.ToArray();
            _encryptor = encryptor;
            _stagingStore = stagingStore;
            _independentStore = independentStore;
        }

        public async Task<CapturedArtifactEvidence> CaptureAsync(
            RecoveryManifestDraft draft,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(draft);
            cancellationToken.ThrowIfCancellationRequested();
            var capability = await ProbeKeyCapabilityAsync(draft.KeyReference, cancellationToken)
                .ConfigureAwait(false);
            ValidateKeyCapability(capability, draft.KeyReference);
            string workspace = null;

            try
            {
                workspace = await _stagingStore.CreatePlaintextWorkspaceAsync(
                    draft.RecoverySetId,
                    cancellationToken).ConfigureAwait(false);
                var plaintextArtifacts = await CaptureSourcesAsync(
                    draft.RecoverySetId,
                    workspace,
                    cancellationToken).ConfigureAwait(false);
                var protectedArtifacts = new List<StagedRecoveryArtifact>(plaintextArtifacts.Count);
                foreach (var plaintext in plaintextArtifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    protectedArtifacts.Add(await ProtectAsync(
                        draft,
                        plaintext,
                        capability,
                        cancellationToken).ConfigureAwait(false));
                }

                return new CapturedArtifactEvidence(
                    draft.RecoverySetId,
                    JsonSerializer.Serialize(protectedArtifacts, JsonOptions));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await QuarantineStagingAsync(draft.RecoverySetId, "capture-cancelled").ConfigureAwait(false);
                throw;
            }
            catch (ArtifactProtectionException)
            {
                await QuarantineStagingAsync(draft.RecoverySetId, "capture-failed").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (IsCaptureFailure(exception))
            {
                await QuarantineStagingAsync(draft.RecoverySetId, "capture-failed").ConfigureAwait(false);
                throw Failure("capture-source-failed", "Artifact capture failed.");
            }
            finally
            {
                if (workspace is not null)
                {
                    await _stagingStore.CleanupPlaintextAsync(
                        draft.RecoverySetId,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        public async Task<IndependentCopyEvidence> CopyAsync(
            RecoveryManifestDraft draft,
            CapturedArtifactEvidence capture,
            CancellationToken cancellationToken)
        {
            ValidateCaptureIdentity(draft, capture);
            var store = await ProbeIndependentStoreAsync(cancellationToken).ConfigureAwait(false);
            ValidateIndependentStore(store);
            var artifacts = ReadInventory(capture.InventoryJson);

            try
            {
                foreach (var artifact in artifacts)
                {
                    await using var content = await _stagingStore.OpenProtectedReadAsync(
                        artifact.ProtectedSha256,
                        cancellationToken).ConfigureAwait(false);
                    var copied = await _independentStore.PutIfAbsentAsync(
                        artifact,
                        content,
                        cancellationToken).ConfigureAwait(false);
                    ValidateCopyEvidence(artifact, copied);
                }

                return new IndependentCopyEvidence(
                    store.StoreId,
                    true,
                    store.ImmutabilityState != ArtifactImmutabilityState.Mutable);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await QuarantineIndependentAsync(draft.RecoverySetId, "copy-cancelled").ConfigureAwait(false);
                throw;
            }
            catch (ArtifactProtectionException)
            {
                await QuarantineIndependentAsync(draft.RecoverySetId, "copy-failed").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (IsStoreFailure(exception))
            {
                await QuarantineIndependentAsync(draft.RecoverySetId, "copy-failed").ConfigureAwait(false);
                throw Failure("independent-copy-failed", "Independent artifact copy failed.");
            }
        }

        public async Task<IntegrityVerificationEvidence> VerifyAsync(
            RecoveryManifestDraft draft,
            CapturedArtifactEvidence capture,
            IndependentCopyEvidence copy,
            CancellationToken cancellationToken)
        {
            ValidateCaptureIdentity(draft, capture);
            var store = await ProbeIndependentStoreAsync(cancellationToken).ConfigureAwait(false);
            ValidateIndependentStore(store);
            if (!string.Equals(copy.CopyIdentity, store.StoreId, StringComparison.Ordinal) ||
                !copy.IsIndependent || !copy.IsImmutable)
            {
                throw Failure("copy-evidence-drift", "Independent copy evidence changed before verification.");
            }

            try
            {
                var artifacts = ReadInventory(capture.InventoryJson);
                foreach (var artifact in artifacts)
                {
                    await VerifyIndependentArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
                }

                var manifest = CreateProtectedManifest(draft, store, artifacts);
                var manifestHash = Sha256(manifest.Span);
                var publication = await _independentStore.PublishManifestIfAbsentAsync(
                    draft.RecoverySetId,
                    manifest,
                    manifestHash,
                    cancellationToken).ConfigureAwait(false);
                ValidatePublication(draft.RecoverySetId, manifestHash, publication);
                return new IntegrityVerificationEvidence(true, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await QuarantineIndependentAsync(draft.RecoverySetId, "verification-cancelled").ConfigureAwait(false);
                throw;
            }
            catch (ArtifactProtectionException)
            {
                await QuarantineIndependentAsync(draft.RecoverySetId, "verification-failed").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (IsStoreFailure(exception))
            {
                await QuarantineIndependentAsync(draft.RecoverySetId, "verification-failed").ConfigureAwait(false);
                throw Failure("independent-verification-failed", "Independent artifact verification failed.");
            }
        }

        private async Task<IReadOnlyCollection<PlaintextRecoveryArtifact>> CaptureSourcesAsync(
            Guid recoverySetId,
            string workspace,
            CancellationToken cancellationToken)
        {
            if (_sources.Length == 0)
            {
                throw Failure("capture-source-missing", "At least one artifact capture source is required.");
            }

            var artifacts = new List<PlaintextRecoveryArtifact>();
            foreach (var source in _sources)
            {
                var captured = await source.CaptureAsync(
                    new RecoveryArtifactCaptureContext(recoverySetId, workspace),
                    cancellationToken).ConfigureAwait(false);
                if (captured is null)
                {
                    throw Failure("capture-source-invalid", "Artifact capture source returned invalid evidence.");
                }

                artifacts.AddRange(captured);
            }

            ValidatePlaintextInventory(artifacts);
            return artifacts;
        }

        private async Task<ArtifactKeyCapability> ProbeKeyCapabilityAsync(
            string keyReference,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _encryptor.ProbeAsync(keyReference, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsStoreFailure(exception))
            {
                throw Failure("key-provider-probe-failed", "Artifact key-provider probe failed.");
            }
        }

        private async Task<ArtifactStoreCapability> ProbeIndependentStoreAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await _independentStore.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsStoreFailure(exception))
            {
                throw Failure("independent-store-probe-failed", "Independent artifact-store probe failed.");
            }
        }

        private async Task<StagedRecoveryArtifact> ProtectAsync(
            RecoveryManifestDraft draft,
            PlaintextRecoveryArtifact plaintext,
            ArtifactKeyCapability capability,
            CancellationToken cancellationToken)
        {
            var associatedData = Encoding.UTF8.GetBytes(
                $"{draft.RecoverySetId:N}\n{plaintext.LogicalName}\n{draft.ManifestChecksum}");
            return await _stagingStore.WriteProtectedAsync(
                draft.RecoverySetId,
                plaintext,
                async (output, token) =>
                {
                    await using var input = new FileStream(
                        plaintext.FilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var evidence = await _encryptor.EncryptAsync(
                        draft.KeyReference,
                        input,
                        output,
                        associatedData,
                        token).ConfigureAwait(false);
                    ValidateEncryptionEvidence(evidence, capability);
                    return evidence;
                },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
