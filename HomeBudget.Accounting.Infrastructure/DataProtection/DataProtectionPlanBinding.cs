using System;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal static class DataProtectionPlanBinding
    {
        public static string ComputeFingerprint(
            string operationKind,
            string environmentId,
            string ledgerId,
            string generationId,
            string scopeInventoryFingerprint = null)
        {
            return scopeInventoryFingerprint is null
                ? Hash(BuildCanonical(operationKind, environmentId, ledgerId, generationId))
                : Hash(BuildCanonical(
                    operationKind,
                    environmentId,
                    ledgerId,
                    generationId,
                    scopeInventoryFingerprint));
        }

        public static string ComputeScopeKey(
            string scopeKind,
            string environmentId,
            string ledgerId,
            string generationId)
        {
            var targetHash = Hash(BuildCanonical(scopeKind, environmentId, ledgerId, generationId));
            return $"{scopeKind}:{targetHash}";
        }

        public static string HashConfirmationToken(string confirmationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(confirmationToken);
            return Hash(confirmationToken);
        }

        private static string BuildCanonical(params string[] values)
        {
            var canonical = new StringBuilder();
            foreach (var value in values)
            {
                var normalized = value ?? string.Empty;
                canonical.Append(Encoding.UTF8.GetByteCount(normalized));
                canonical.Append(':');
                canonical.Append(normalized);
                canonical.Append('\n');
            }

            return canonical.ToString();
        }

        private static string Hash(string value)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }
}
