using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    public enum ArtifactProtectionProfile
    {
        Production,
        DisposableTest
    }

    public enum ArtifactTransportSecurity
    {
        None,
        LocalProcessOnly,
        Tls12OrHigher
    }

    public enum ArtifactImmutabilityState
    {
        Mutable,
        Immutable,
        Offline
    }

    public sealed record RecoveryArtifactCaptureContext(Guid RecoverySetId, string PlaintextRoot);

    public sealed record PlaintextRecoveryArtifact(
        string LogicalName,
        string FilePath,
        IReadOnlyCollection<string> Dependencies);

    public sealed record ArtifactKeyCapability(
        string ProviderId,
        string ProviderVersion,
        string KeyReference,
        bool SupportsStreamingAuthenticatedEncryption,
        bool IsSeparatelyRecoverable,
        bool ExportsKeyMaterial);

    public sealed record ArtifactEncryptionEvidence(
        string ProviderId,
        string ProviderVersion,
        string KeyReference,
        string Algorithm,
        string EnvelopeVersion,
        bool IsAuthenticated,
        bool IsStreaming);

    public sealed record ArtifactStoreCapability(
        string ProviderId,
        string ProviderVersion,
        string StoreId,
        string HostIdentity,
        string FailureDomainIdentity,
        ArtifactTransportSecurity TransportSecurity,
        ArtifactImmutabilityState ImmutabilityState,
        bool IsTestOnlyFileSystem,
        bool DisposableRootVerified);

    public sealed record StagedRecoveryArtifact(
        string LogicalName,
        long PlaintextSize,
        string PlaintextSha256,
        long ProtectedSize,
        string ProtectedSha256,
        ArtifactEncryptionEvidence Encryption,
        IReadOnlyCollection<string> Dependencies);

    public sealed record IndependentArtifactEvidence(
        string LogicalName,
        string ProtectedSha256,
        long ProtectedSize,
        bool IsImmutable,
        bool IsOffline);

    public sealed record ManifestPublicationEvidence(
        Guid RecoverySetId,
        string ManifestSha256,
        bool PublishedAtomically,
        bool PublishedConditionally);
}
