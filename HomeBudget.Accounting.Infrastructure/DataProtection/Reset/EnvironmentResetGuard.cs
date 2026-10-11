#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Reset
{
    public sealed class EnvironmentResetGuard
    {
        private static readonly string[] NameOnlyIdentities =
        [
            "localhost",
            "127.0.0.1",
            "::1",
            "docker",
            "host.docker.internal"
        ];

        private readonly IEnvironmentResetTargetVerifier _targetVerifier;

        public EnvironmentResetGuard(IEnvironmentResetTargetVerifier targetVerifier)
        {
            ArgumentNullException.ThrowIfNull(targetVerifier);
            _targetVerifier = targetVerifier;
        }

        public async Task<EnvironmentResetPlan> ValidateAsync(
            EnvironmentResetPlan plan,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (!string.Equals(
                    plan.PlanFingerprint,
                    EnvironmentResetPlanBinding.ComputeFingerprint(plan),
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    "environment-plan-fingerprint-mismatch",
                    "The environment reset plan fingerprint is stale or invalid.");
            }

            if (plan.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(plan.EnvironmentId) ||
                string.IsNullOrWhiteSpace(plan.EnvironmentMarker) ||
                IsNameOnly(plan.HostResourceId) || IsNameOnly(plan.StorageResourceId))
            {
                throw Invalid(
                    "environment-target-not-positively-identified",
                    "Environment reset requires verified host and storage resource identities.");
            }

            if (plan.AuthenticationUtc.Offset != TimeSpan.Zero || plan.ConfirmedUtc.Offset != TimeSpan.Zero ||
                plan.RecentAuthenticationWindow <= TimeSpan.Zero ||
                plan.ConfirmedUtc < plan.AuthenticationUtc ||
                plan.ConfirmedUtc - plan.AuthenticationUtc > plan.RecentAuthenticationWindow)
            {
                throw Invalid("environment-recent-auth-required", "Environment reset requires recent authentication.");
            }

            if (plan.PreBackupRecoverySetId == Guid.Empty || !plan.PreBackupIntegrityVerified ||
                !plan.PreBackupIndependentlyProtected || plan.PreBackupCutoffUtc.Offset != TimeSpan.Zero ||
                plan.PreBackupCutoffUtc < plan.ConfirmedUtc)
            {
                throw Invalid(
                    "environment-prebackup-not-protected",
                    "Environment reset requires a fresh verified independently protected recovery set.");
            }

            if (plan.AffectedEntityCounts is null || plan.AffectedEntityCounts.Values.Any(value => value < 0) ||
                plan.Resources is null || plan.Resources.Count == 0 ||
                plan.Resources.Select(resource => resource.ResourceId).Distinct(StringComparer.Ordinal).Count() !=
                    plan.Resources.Count)
            {
                throw Invalid("environment-plan-invalid", "Environment reset inventory is invalid.");
            }

            if (plan.Resources.Any(resource =>
                string.IsNullOrWhiteSpace(resource.ResourceId) || string.IsNullOrWhiteSpace(resource.ResourceKind)))
            {
                throw Invalid("environment-plan-invalid", "Environment reset inventory is invalid.");
            }

            if (plan.PreservedPolicies is null ||
                !plan.PreservedPolicies.Contains("administrator-access", StringComparer.Ordinal) ||
                !plan.PreservedPolicies.Contains("deployment-configuration", StringComparer.Ordinal) ||
                !plan.PreservedPolicies.Contains("protected-audit", StringComparer.Ordinal))
            {
                throw Invalid(
                    "environment-preservation-policy-invalid",
                    "Environment reset must preserve administrator access, deployment configuration, and protected audit.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var verification = await _targetVerifier.InspectAsync(plan, cancellationToken).ConfigureAwait(false);
            ValidateProviderEvidence(plan, verification);
            return plan;
        }

        private static void ValidateProviderEvidence(
            EnvironmentResetPlan plan,
            EnvironmentResetTargetVerification verification)
        {
            if (verification is null || string.IsNullOrWhiteSpace(verification.EvidenceProvider) ||
                verification.InspectedUtc.Offset != TimeSpan.Zero || verification.InspectedUtc > plan.ConfirmedUtc ||
                !string.Equals(verification.PlanFingerprint, plan.PlanFingerprint, StringComparison.Ordinal) ||
                !string.Equals(verification.EnvironmentMarker, plan.EnvironmentMarker, StringComparison.Ordinal) ||
                !string.Equals(verification.HostResourceId, plan.HostResourceId, StringComparison.Ordinal) ||
                !string.Equals(verification.StorageResourceId, plan.StorageResourceId, StringComparison.Ordinal) ||
                verification.Resources is null)
            {
                throw Invalid(
                    "environment-verification-invalid",
                    "Environment reset requires current provider-produced evidence bound to the exact plan.");
            }

            var expectedIds = plan.Resources.Select(resource => resource.ResourceId).OrderBy(id => id, StringComparer.Ordinal);
            var verifiedIds = verification.Resources.Select(resource => resource.ResourceId)
                .OrderBy(id => id, StringComparer.Ordinal);
            if (!expectedIds.SequenceEqual(verifiedIds, StringComparer.Ordinal) ||
                verification.Resources.Any(resource =>
                    !resource.IsDedicated ||
                    !resource.IsOwnedByHomeLedger ||
                    IsNameOnly(resource.ResolvedStorageIdentity) ||
                    !IsSha256(resource.OwnershipProofSha256)))
            {
                throw Invalid(
                    "environment-resource-not-dedicated",
                    "Provider inspection did not prove every target is dedicated, owned, and positively identified.");
            }
        }

        private static bool IsNameOnly(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                NameOnlyIdentities.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsSha256(string value) =>
            value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static EnvironmentResetException Invalid(string code, string message) => new(code, message);
    }
}
