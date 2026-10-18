using System;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public static class ProjectionRebuildPlanBinding
    {
        public static string ComputeOperationFingerprint(
            string environmentId,
            string ledgerId,
            string generationId,
            string scopeInventoryFingerprint)
        {
            ValidateInventoryFingerprint(scopeInventoryFingerprint);
            return DataProtectionPlanBinding.ComputeFingerprint(
                DataProtectionOperationKinds.ProjectionRebuild,
                environmentId,
                ledgerId,
                generationId,
                scopeInventoryFingerprint);
        }

        public static void ValidateInventoryFingerprint(string fingerprint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
            if (fingerprint.Length != 64 || fingerprint.Any(character => !IsLowerHexDigit(character)))
            {
                throw new ArgumentException(
                    "Projection rebuild scope inventory fingerprint must be a lowercase SHA-256 value.",
                    nameof(fingerprint));
            }
        }

        private static bool IsLowerHexDigit(char value) =>
            (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');
    }
}
