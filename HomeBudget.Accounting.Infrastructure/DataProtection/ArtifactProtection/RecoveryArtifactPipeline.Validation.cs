using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    internal sealed partial class RecoveryArtifactPipeline
    {
        private static void ValidatePolicy(ArtifactProtectionPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(policy.EnvironmentId) ||
                string.IsNullOrWhiteSpace(policy.SourceHostIdentity) ||
                string.IsNullOrWhiteSpace(policy.SourceFailureDomainIdentity) ||
                policy.MaximumArtifactCount <= 0 || policy.MaximumArtifactBytes <= 0)
            {
                throw new ArgumentException("Artifact protection policy is invalid.", nameof(policy));
            }
        }

        private static void ValidateKeyCapability(ArtifactKeyCapability capability, string keyReference)
        {
            if (capability is null || string.IsNullOrWhiteSpace(capability.ProviderId) ||
                string.IsNullOrWhiteSpace(capability.ProviderVersion) ||
                !string.Equals(capability.KeyReference, keyReference, StringComparison.Ordinal) ||
                !capability.SupportsStreamingAuthenticatedEncryption || capability.ExportsKeyMaterial)
            {
                throw Failure("key-capability-invalid", "Artifact key-provider capability is invalid.");
            }

            if (!capability.IsSeparatelyRecoverable)
            {
                throw Failure(
                    "key-not-separately-recoverable",
                    "Artifact encryption key is not separately recoverable.");
            }
        }

        private void ValidateIndependentStore(ArtifactStoreCapability capability)
        {
            if (capability is null || string.IsNullOrWhiteSpace(capability.ProviderId) ||
                string.IsNullOrWhiteSpace(capability.ProviderVersion) ||
                string.IsNullOrWhiteSpace(capability.StoreId) ||
                string.IsNullOrWhiteSpace(capability.HostIdentity) ||
                string.IsNullOrWhiteSpace(capability.FailureDomainIdentity))
            {
                throw Failure("store-capability-invalid", "Independent-store capability is invalid.");
            }

            if (capability.IsTestOnlyFileSystem)
            {
                if (_policy.Profile != ArtifactProtectionProfile.DisposableTest ||
                    !capability.DisposableRootVerified ||
                    capability.TransportSecurity != ArtifactTransportSecurity.LocalProcessOnly)
                {
                    throw Failure("filesystem-production-forbidden", "Filesystem artifact storage is test-only.");
                }
            }
            else if (capability.TransportSecurity != ArtifactTransportSecurity.Tls12OrHigher)
            {
                throw Failure("transport-protection-required", "Protected transport is required.");
            }

            if (string.Equals(capability.HostIdentity, _policy.SourceHostIdentity, StringComparison.Ordinal) ||
                string.Equals(
                    capability.FailureDomainIdentity,
                    _policy.SourceFailureDomainIdentity,
                    StringComparison.Ordinal))
            {
                throw Failure("store-not-independent", "Artifact store is not in an independent failure domain.");
            }

            if (capability.ImmutabilityState == ArtifactImmutabilityState.Mutable)
            {
                throw Failure("immutable-or-offline-copy-required", "Immutable or offline protection is required.");
            }
        }

        private void ValidatePlaintextInventory(List<PlaintextRecoveryArtifact> artifacts)
        {
            if (artifacts.Count == 0 || artifacts.Count > _policy.MaximumArtifactCount ||
                artifacts.Any(artifact => artifact is null || string.IsNullOrWhiteSpace(artifact.LogicalName)) ||
                artifacts.GroupBy(artifact => artifact.LogicalName, StringComparer.Ordinal).Any(group => group.Count() > 1))
            {
                throw Failure("capture-inventory-invalid", "Artifact capture inventory is invalid.");
            }

            long totalBytes = 0;
            foreach (var artifact in artifacts)
            {
                var file = new FileInfo(artifact.FilePath);
                if (!file.Exists || file.Length < 0 || totalBytes > _policy.MaximumArtifactBytes - file.Length)
                {
                    throw Failure("capture-resource-limit", "Artifact capture exceeds configured limits.");
                }

                totalBytes += file.Length;
            }
        }

        private static void ValidateEncryptionEvidence(
            ArtifactEncryptionEvidence evidence,
            ArtifactKeyCapability capability)
        {
            if (evidence is null || !evidence.IsAuthenticated || !evidence.IsStreaming ||
                string.IsNullOrWhiteSpace(evidence.Algorithm) ||
                string.IsNullOrWhiteSpace(evidence.EnvelopeVersion) ||
                !string.Equals(evidence.ProviderId, capability.ProviderId, StringComparison.Ordinal) ||
                !string.Equals(evidence.ProviderVersion, capability.ProviderVersion, StringComparison.Ordinal) ||
                !string.Equals(evidence.KeyReference, capability.KeyReference, StringComparison.Ordinal))
            {
                throw Failure("encryption-evidence-invalid", "Artifact encryption evidence is invalid.");
            }
        }

        private static void ValidateCaptureIdentity(
            RecoveryManifestDraft draft,
            CapturedArtifactEvidence capture)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(capture);
            if (draft.RecoverySetId != capture.RecoverySetId)
            {
                throw Failure("capture-identity-mismatch", "Artifact capture identity is invalid.");
            }
        }

        private StagedRecoveryArtifact[] ReadInventory(string inventoryJson)
        {
            StagedRecoveryArtifact[] artifacts;
            try
            {
                artifacts = JsonSerializer.Deserialize<StagedRecoveryArtifact[]>(inventoryJson, JsonOptions);
            }
            catch (JsonException)
            {
                throw Failure("protected-inventory-invalid", "Protected artifact inventory is invalid.");
            }

            if (artifacts is null || artifacts.Length == 0 || artifacts.Length > _policy.MaximumArtifactCount ||
                artifacts.Any(artifact => artifact is null || artifact.ProtectedSize < 0 || artifact.PlaintextSize < 0 ||
                    string.IsNullOrWhiteSpace(artifact.ProtectedSha256) ||
                    string.IsNullOrWhiteSpace(artifact.PlaintextSha256)))
            {
                throw Failure("protected-inventory-invalid", "Protected artifact inventory is invalid.");
            }

            return artifacts;
        }

        private static void ValidateCopyEvidence(
            StagedRecoveryArtifact expected,
            IndependentArtifactEvidence actual)
        {
            if (actual is null || !string.Equals(actual.LogicalName, expected.LogicalName, StringComparison.Ordinal) ||
                !string.Equals(actual.ProtectedSha256, expected.ProtectedSha256, StringComparison.Ordinal) ||
                actual.ProtectedSize != expected.ProtectedSize || (!actual.IsImmutable && !actual.IsOffline))
            {
                throw Failure("copy-evidence-invalid", "Independent artifact copy evidence is invalid.");
            }
        }

        private async Task VerifyIndependentArtifactAsync(
            StagedRecoveryArtifact artifact,
            CancellationToken cancellationToken)
        {
            await using var stream = await _independentStore.OpenReadAsync(
                artifact.ProtectedSha256,
                cancellationToken).ConfigureAwait(false);
            var evidence = await HashAsync(stream, cancellationToken).ConfigureAwait(false);
            if (evidence.Size != artifact.ProtectedSize ||
                !string.Equals(evidence.Sha256, artifact.ProtectedSha256, StringComparison.Ordinal))
            {
                throw Failure("independent-verification-failed", "Independent artifact verification failed.");
            }
        }

        private static ReadOnlyMemory<byte> CreateProtectedManifest(
            RecoveryManifestDraft draft,
            ArtifactStoreCapability store,
            IReadOnlyCollection<StagedRecoveryArtifact> artifacts)
        {
            using var applicationManifest = JsonDocument.Parse(draft.ManifestJson);
            return JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = "home-ledger-protected-recovery-set/v1",
                    draft.RecoverySetId,
                    draft.EnvironmentId,
                    draft.LedgerScope,
                    draft.SourceGenerationId,
                    draft.CaptureStartedUtc,
                    draft.CommonCutoffUtc,
                    draft.KeyReference,
                    applicationManifestChecksum = draft.ManifestChecksum,
                    applicationManifest = applicationManifest.RootElement,
                    artifacts,
                    protection = new
                    {
                        store.ProviderId,
                        store.ProviderVersion,
                        store.StoreId,
                        store.HostIdentity,
                        store.FailureDomainIdentity,
                        transportSecurity = store.TransportSecurity.ToString(),
                        verificationState = store.ImmutabilityState.ToString()
                    }
                },
                JsonOptions);
        }

        private static void ValidatePublication(
            Guid recoverySetId,
            string expectedHash,
            ManifestPublicationEvidence publication)
        {
            if (publication is null || publication.RecoverySetId != recoverySetId ||
                !string.Equals(publication.ManifestSha256, expectedHash, StringComparison.Ordinal) ||
                !publication.PublishedAtomically || !publication.PublishedConditionally)
            {
                throw Failure("manifest-publication-invalid", "Recovery manifest publication evidence is invalid.");
            }
        }

        private static async Task<HashEvidence> HashAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long length = 0;
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    length += read;
                }

                return new HashEvidence(
                    length,
                    Convert.ToHexString(hash.GetHashAndReset()));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private Task QuarantineStagingAsync(Guid recoverySetId, string reasonCode) =>
            _stagingStore.QuarantineAsync(recoverySetId, reasonCode, CancellationToken.None);

        private Task QuarantineIndependentAsync(Guid recoverySetId, string reasonCode) =>
            _independentStore.QuarantineAsync(recoverySetId, reasonCode, CancellationToken.None);

        private static bool IsCaptureFailure(Exception exception) =>
            exception is not OperationCanceledException and not ArtifactProtectionException;

        private static bool IsStoreFailure(Exception exception) =>
            exception is not OperationCanceledException and not ArtifactProtectionException;

        private static string Sha256(ReadOnlySpan<byte> content) =>
            Convert.ToHexString(SHA256.HashData(content));

        private static ArtifactProtectionException Failure(string code, string message) => new(code, message);

        private sealed record HashEvidence(long Size, string Sha256);
    }
}
