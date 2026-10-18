#nullable enable

using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Configuration
{
    public static class DataProtectionRecoveryReadiness
    {
        public static IReadOnlyList<string> Evaluate(
            DataProtectionRecoveryOptions options,
            DataProtectionRuntimeReadinessEvidence runtimeEvidence)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(runtimeEvidence);
            var missing = new List<string>(11);
            AddIf(missing, !options.Enabled, "data-protection-disabled");
            AddIf(missing, string.IsNullOrWhiteSpace(options.BackupProvider), "backup-provider-not-configured");
            AddIf(missing, string.IsNullOrWhiteSpace(options.IndependentStore), "independent-store-not-configured");
            AddIf(missing, string.IsNullOrWhiteSpace(options.KeyReference), "key-reference-not-configured");
            AddIf(missing, string.IsNullOrWhiteSpace(options.NotificationTarget), "notification-target-not-configured");
            AddIf(missing, string.IsNullOrWhiteSpace(options.RecoveryHostResourceId), "recovery-host-not-configured");
            AddIfConfiguredAndUnvalidated(
                missing,
                options.BackupProvider,
                runtimeEvidence.BackupProviderReference,
                "backup-provider-not-validated");
            AddIfConfiguredAndUnvalidated(
                missing,
                options.IndependentStore,
                runtimeEvidence.IndependentStoreReference,
                "independent-store-not-validated");
            AddIfConfiguredAndUnvalidated(
                missing,
                options.KeyReference,
                runtimeEvidence.KeyReference,
                "key-reference-not-validated");
            AddIfConfiguredAndUnvalidated(
                missing,
                options.NotificationTarget,
                runtimeEvidence.NotificationTargetReference,
                "notification-target-not-validated");
            AddIfConfiguredAndUnvalidated(
                missing,
                options.RecoveryHostResourceId,
                runtimeEvidence.RecoveryHostResourceId,
                "recovery-host-not-validated");
            return missing.AsReadOnly();
        }

        private static void AddIfConfiguredAndUnvalidated(
            List<string> missing,
            string configuredReference,
            string? validatedReference,
            string identifier)
        {
            AddIf(
                missing,
                !string.IsNullOrWhiteSpace(configuredReference) &&
                !string.Equals(configuredReference, validatedReference, StringComparison.Ordinal),
                identifier);
        }

        private static void AddIf(List<string> missing, bool condition, string identifier)
        {
            if (condition)
            {
                missing.Add(identifier);
            }
        }
    }
}
