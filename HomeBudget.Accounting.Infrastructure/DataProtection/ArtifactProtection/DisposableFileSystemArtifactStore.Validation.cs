using System;
using System.IO;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    internal sealed partial class DisposableFileSystemArtifactStore
    {
        private static string ValidateRoot(DisposableArtifactRootIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            var parent = Path.GetFullPath(Require(identity.ExpectedParentPath));
            var root = Path.GetFullPath(Require(identity.RootPath));
            if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !Directory.Exists(root) || IsReparsePoint(root))
            {
                throw Failure("disposable-root-unverified", "Disposable artifact root evidence is invalid.");
            }

            var marker = Path.Combine(root, MarkerFileName);
            if (!File.Exists(marker) || IsReparsePoint(marker) ||
                !string.Equals(
                    File.ReadAllText(marker),
                    $"{Require(identity.RootId)}\n{Require(identity.OwnershipToken)}",
                    StringComparison.Ordinal))
            {
                throw Failure("disposable-root-unverified", "Disposable artifact root evidence is invalid.");
            }

            return root;
        }

        private static ArtifactStoreCapability ValidateCapability(ArtifactStoreCapability capability)
        {
            if (capability is null || !capability.IsTestOnlyFileSystem || !capability.DisposableRootVerified ||
                capability.TransportSecurity != ArtifactTransportSecurity.LocalProcessOnly ||
                capability.ImmutabilityState == ArtifactImmutabilityState.Mutable ||
                new[]
                {
                    capability.ProviderId,
                    capability.ProviderVersion,
                    capability.StoreId,
                    capability.HostIdentity,
                    capability.FailureDomainIdentity
                }.Any(string.IsNullOrWhiteSpace))
            {
                throw Failure("filesystem-capability-invalid", "Disposable filesystem capability is invalid.");
            }

            return capability;
        }

        private static void ValidateMarker(string root, string expectedMarker)
        {
            var marker = Path.Combine(root, MarkerFileName);
            if (!File.Exists(marker) || IsReparsePoint(marker) ||
                !string.Equals(File.ReadAllText(marker), expectedMarker, StringComparison.Ordinal))
            {
                throw Failure("disposable-root-unverified", "Disposable artifact root evidence is invalid.");
            }
        }

        private static string ValidateContainedFile(string candidate, string expectedRoot)
        {
            var root = Path.GetFullPath(expectedRoot);
            var resolved = Path.GetFullPath(Require(candidate));
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !File.Exists(resolved) || IsReparsePoint(resolved))
            {
                throw Failure("plaintext-path-invalid", "Plaintext artifact path is invalid.");
            }

            return resolved;
        }

        private static void ValidateLogicalName(string logicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName) || logicalName.Length > 512 ||
                logicalName.Contains('\\', StringComparison.Ordinal) ||
                logicalName[0] == '/' ||
                logicalName.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw Failure("logical-name-invalid", "Artifact logical name is invalid.");
            }
        }

        private static string ValidateSha256(string sha256)
        {
            if (sha256?.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw Failure("content-address-invalid", "Artifact content address is invalid.");
            }

            return sha256.ToUpperInvariant();
        }

        private static bool IsReparsePoint(string path) =>
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        private static string Require(string value) =>
            !string.IsNullOrWhiteSpace(value)
                ? value
                : throw Failure("disposable-root-unverified", "Disposable artifact root evidence is invalid.");
    }
}
