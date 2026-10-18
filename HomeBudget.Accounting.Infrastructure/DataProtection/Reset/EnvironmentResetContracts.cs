#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Reset
{
    public sealed record EnvironmentResetResource(
        string ResourceId,
        string ResourceKind);

    public sealed record EnvironmentResetVerifiedResource(
        string ResourceId,
        bool IsDedicated,
        bool IsOwnedByHomeLedger,
        string ResolvedStorageIdentity,
        string OwnershipProofSha256);

    public sealed record EnvironmentResetTargetVerification(
        string EvidenceProvider,
        DateTimeOffset InspectedUtc,
        string PlanFingerprint,
        string EnvironmentMarker,
        string HostResourceId,
        string StorageResourceId,
        IReadOnlyList<EnvironmentResetVerifiedResource> Resources);

    public interface IEnvironmentResetTargetVerifier
    {
        Task<EnvironmentResetTargetVerification> InspectAsync(
            EnvironmentResetPlan plan,
            CancellationToken cancellationToken);
    }

    public sealed record EnvironmentResetPlan(
        Guid OperationId,
        string EnvironmentId,
        string EnvironmentMarker,
        string HostResourceId,
        string StorageResourceId,
        DateTimeOffset AuthenticationUtc,
        DateTimeOffset ConfirmedUtc,
        TimeSpan RecentAuthenticationWindow,
        Guid PreBackupRecoverySetId,
        bool PreBackupIntegrityVerified,
        bool PreBackupIndependentlyProtected,
        DateTimeOffset PreBackupCutoffUtc,
        IReadOnlyDictionary<string, long> AffectedEntityCounts,
        IReadOnlyList<EnvironmentResetResource> Resources,
        IReadOnlyList<string> PreservedPolicies,
        string PlanFingerprint);

    public sealed class EnvironmentResetException : InvalidOperationException
    {
        public EnvironmentResetException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
