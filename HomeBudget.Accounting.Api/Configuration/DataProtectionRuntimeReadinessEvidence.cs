#nullable enable

namespace HomeBudget.Accounting.Api.Configuration
{
    public sealed class DataProtectionRuntimeReadinessEvidence
    {
        public static DataProtectionRuntimeReadinessEvidence Unavailable { get; } =
            new(null, null, null, null, null);

        public DataProtectionRuntimeReadinessEvidence(
            string? backupProviderReference,
            string? independentStoreReference,
            string? keyReference,
            string? notificationTargetReference,
            string? recoveryHostResourceId)
        {
            BackupProviderReference = backupProviderReference;
            IndependentStoreReference = independentStoreReference;
            KeyReference = keyReference;
            NotificationTargetReference = notificationTargetReference;
            RecoveryHostResourceId = recoveryHostResourceId;
        }

        public string? BackupProviderReference { get; }

        public string? IndependentStoreReference { get; }

        public string? KeyReference { get; }

        public string? NotificationTargetReference { get; }

        public string? RecoveryHostResourceId { get; }
    }
}
