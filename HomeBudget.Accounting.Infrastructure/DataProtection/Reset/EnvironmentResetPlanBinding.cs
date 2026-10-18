#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Reset
{
    public static class EnvironmentResetPlanBinding
    {
        public static string ComputeFingerprint(EnvironmentResetPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var builder = new StringBuilder()
                .AppendLine("home-ledger-environment-reset-plan/v1")
                .AppendLine(plan.OperationId.ToString("D"))
                .AppendLine(plan.EnvironmentId)
                .AppendLine(plan.EnvironmentMarker)
                .AppendLine(plan.HostResourceId)
                .AppendLine(plan.StorageResourceId)
                .AppendLine(plan.AuthenticationUtc.ToUniversalTime().ToString("O"))
                .AppendLine(plan.ConfirmedUtc.ToUniversalTime().ToString("O"))
                .AppendLine(plan.RecentAuthenticationWindow.ToString("c"))
                .AppendLine(plan.PreBackupRecoverySetId.ToString("D"))
                .AppendLine(plan.PreBackupIntegrityVerified.ToString())
                .AppendLine(plan.PreBackupIndependentlyProtected.ToString())
                .AppendLine(plan.PreBackupCutoffUtc.ToUniversalTime().ToString("O"));
            foreach (var count in plan.AffectedEntityCounts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.Append("count:").Append(count.Key).Append('=').Append(count.Value).AppendLine();
            }

            foreach (var resource in plan.Resources.OrderBy(item => item.ResourceId, StringComparer.Ordinal))
            {
                builder.Append("resource:")
                    .Append(resource.ResourceId).Append('|')
                    .Append(resource.ResourceKind)
                    .AppendLine();
            }

            foreach (var policy in plan.PreservedPolicies.OrderBy(item => item, StringComparer.Ordinal))
            {
                builder.Append("preserve:").AppendLine(policy);
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
                .ToLowerInvariant();
        }
    }
}
