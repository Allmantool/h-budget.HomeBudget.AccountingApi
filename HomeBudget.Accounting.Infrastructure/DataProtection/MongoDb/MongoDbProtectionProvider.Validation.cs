using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using MongoDB.Driver;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed partial class MongoDbProtectionProvider
    {
        private string GetTopologyBlocker(MongoDbTopologyEvidence topology)
        {
            if (topology is null || string.IsNullOrWhiteSpace(topology.ServerVersion))
            {
                return "topology-probe-invalid";
            }

            if (!_policy.AllowedServerVersions.Contains(topology.ServerVersion, StringComparer.Ordinal))
            {
                return "unsupported-version";
            }

            if (topology.Topology == MongoDbDeploymentTopology.Standalone)
            {
                return "standalone-topology";
            }

            if (topology.Topology != MongoDbDeploymentTopology.ReplicaSet ||
                string.IsNullOrWhiteSpace(topology.ReplicaSetName))
            {
                return "unsupported-topology";
            }

            return string.Equals(
                topology.DeploymentId,
                _policy.ExpectedDeploymentId,
                StringComparison.Ordinal)
                ? null
                : "source-identity-mismatch";
        }

        private string GetProviderBlocker(
            MongoDbProviderCapabilityEvidence provider,
            MongoDbTopologyEvidence topology)
        {
            if (provider is null ||
                string.IsNullOrWhiteSpace(provider.ProviderVersion) ||
                !_policy.AllowedProviderIds.Contains(provider.ProviderId, StringComparer.Ordinal))
            {
                return "provider-not-allowlisted";
            }

            if (!string.Equals(provider.SourceDeploymentId, topology.DeploymentId, StringComparison.Ordinal))
            {
                return "source-identity-mismatch";
            }

            if (provider.Mechanism == MongoDbProtectionMechanism.LogicalDump)
            {
                return "logical-dump-not-pitr";
            }

            if (provider.Mechanism != MongoDbProtectionMechanism.ContinuousBackup ||
                !provider.SupportsPointInTimeRecovery)
            {
                return "continuous-history-unavailable";
            }

            if (!provider.UsesImmutableArtifacts)
            {
                return "immutable-artifacts-unavailable";
            }

            return IsValidInterval(provider.RecoverableFromUtc, provider.RecoverableToUtc) &&
                AreValidGaps(provider.Gaps, provider.RecoverableFromUtc, provider.RecoverableToUtc)
                ? null
                : "provider-evidence-invalid";
        }

        private void ValidateSnapshot(MongoDbSnapshotEvidence snapshot, MongoDbPitrCapability capability)
        {
            if (snapshot is null || !snapshot.IsVerified)
            {
                throw Failure("snapshot-not-verified", "Verified MongoDB snapshot evidence is required.");
            }

            ValidateProviderIdentity(
                snapshot.ProviderId,
                snapshot.SourceDeploymentId,
                capability,
                "source-identity-mismatch");
            if (!IsIdentifier(snapshot.SnapshotId) || !IsIdentifier(snapshot.ProviderArtifactId))
            {
                throw Failure("snapshot-identity-invalid", "MongoDB snapshot identity evidence is invalid.");
            }

            if (!snapshot.IsImmutable)
            {
                throw Failure("snapshot-not-immutable", "The MongoDB snapshot artifact is not immutable.");
            }

            if (!IsValidInterval(snapshot.MutationFromUtc, snapshot.MutationToUtc) ||
                !IsValidInterval(snapshot.RecoverableFromUtc, snapshot.RecoverableToUtc) ||
                !IsUtc(snapshot.SnapshotTimeUtc) ||
                snapshot.SnapshotTimeUtc < snapshot.MutationFromUtc ||
                snapshot.SnapshotTimeUtc > snapshot.MutationToUtc ||
                snapshot.RecoverableFromUtc < capability.RecoverableFromUtc ||
                snapshot.RecoverableToUtc > capability.RecoverableToUtc ||
                !AreValidGaps(snapshot.Gaps, snapshot.RecoverableFromUtc, snapshot.RecoverableToUtc))
            {
                throw Failure("snapshot-evidence-invalid", "MongoDB snapshot coverage evidence is invalid.");
            }
        }

        private static void ValidateRequestedCutoff(
            DateTimeOffset requestedCutoffUtc,
            MongoDbPitrCapability capability)
        {
            if (requestedCutoffUtc < capability.RecoverableFromUtc ||
                requestedCutoffUtc > capability.RecoverableToUtc)
            {
                throw Failure("cutoff-unavailable", "The requested MongoDB recovery cutoff is unavailable.");
            }

            if (capability.Gaps.Any(gap => Contains(gap, requestedCutoffUtc)))
            {
                throw Failure("recovery-gap", "The requested MongoDB recovery cutoff intersects a gap.");
            }
        }

        private void ValidateRecoveryPoint(
            MongoDbRecoveryPointEvidence evidence,
            MongoDbPitrCapability capability,
            DateTimeOffset requestedCutoffUtc)
        {
            if (evidence is null || !evidence.IsVerified ||
                evidence.RequestedCutoffUtc != requestedCutoffUtc)
            {
                throw Failure("recovery-evidence-mismatch", "MongoDB recovery evidence does not match the request.");
            }

            ValidateProviderIdentity(
                evidence.ProviderId,
                evidence.SourceDeploymentId,
                capability,
                "recovery-evidence-mismatch");
            if (!IsIdentifier(evidence.SnapshotId) || !IsIdentifier(evidence.ProviderArtifactId) ||
                !string.Equals(evidence.Precision, "mongodb-timestamp", StringComparison.Ordinal))
            {
                throw Failure("recovery-evidence-invalid", "MongoDB recovery evidence is invalid.");
            }

            if (!IsUtc(evidence.ActualCutoffUtc) || evidence.ActualCutoffUtc > requestedCutoffUtc ||
                evidence.ActualCutoffUtc < capability.RecoverableFromUtc ||
                evidence.ActualCutoffUtc > capability.RecoverableToUtc ||
                capability.Gaps.Any(gap => Contains(gap, evidence.ActualCutoffUtc)))
            {
                throw Failure("actual-cutoff-invalid", "The actual MongoDB recovery cutoff is invalid.");
            }

            if (!IsUtc(evidence.SnapshotTimeUtc) ||
                !IsValidInterval(evidence.MutationFromUtc, evidence.MutationToUtc) ||
                evidence.SnapshotTimeUtc < evidence.MutationFromUtc ||
                evidence.SnapshotTimeUtc > evidence.ActualCutoffUtc ||
                evidence.ActualCutoffUtc > evidence.MutationToUtc)
            {
                throw Failure("recovery-evidence-invalid", "MongoDB mutation-history evidence is invalid.");
            }
        }

        private static void ValidateProviderIdentity(
            string providerId,
            string sourceDeploymentId,
            MongoDbPitrCapability capability,
            string failureCode)
        {
            if (!string.Equals(providerId, capability.ProviderId, StringComparison.Ordinal) ||
                !string.Equals(sourceDeploymentId, capability.DeploymentId, StringComparison.Ordinal))
            {
                throw Failure(failureCode, "MongoDB provider identity evidence is inconsistent.");
            }
        }

        private static bool IsValidInterval(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
            IsUtc(fromUtc) && IsUtc(toUtc) && fromUtc <= toUtc;

        private static bool AreValidGaps(
            System.Collections.Generic.IReadOnlyCollection<MongoDbRecoveryGap> gaps,
            DateTimeOffset intervalFromUtc,
            DateTimeOffset intervalToUtc)
        {
            if (gaps is null)
            {
                return false;
            }

            DateTimeOffset? priorEnd = null;
            foreach (var gap in gaps.OrderBy(gap => gap.FromUtc))
            {
                if (gap is null || !IsUtc(gap.FromUtc) || !IsUtc(gap.ToUtc) ||
                    gap.FromUtc >= gap.ToUtc || gap.FromUtc < intervalFromUtc ||
                    gap.ToUtc > intervalToUtc || !IsIdentifier(gap.ReasonCode) ||
                    priorEnd > gap.FromUtc)
                {
                    return false;
                }

                priorEnd = gap.ToUtc;
            }

            return true;
        }

        private static bool Contains(MongoDbRecoveryGap gap, DateTimeOffset cutoffUtc) =>
            cutoffUtc >= gap.FromUtc && cutoffUtc < gap.ToUtc;

        private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

        private static bool IsIdentifier(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && !value.Any(char.IsControl);

        private static MongoDbPitrCapability Blocked(
            MongoDbTopologyEvidence topology,
            string reasonCode,
            string providerId = null) => new(
                MongoDbPitrCapabilityState.Blocked,
                reasonCode,
                topology?.ServerVersion,
                topology?.Topology ?? MongoDbDeploymentTopology.Unknown,
                topology?.DeploymentId,
                providerId,
                null,
                null,
                Array.Empty<MongoDbRecoveryGap>());

        private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            return exception is MongoException or IOException or HttpRequestException or
                TimeoutException or InvalidOperationException or UnauthorizedAccessException or
                CryptographicException;
        }

        private static MongoDbProtectionException Failure(string code, string safeMessage) => new(code, safeMessage);
    }
}
