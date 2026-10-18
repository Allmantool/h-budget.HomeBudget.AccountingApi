#nullable enable

using System;

namespace HomeBudget.Accounting.Api.Configuration
{
    public sealed class DataProtectionRecoveryOptions
    {
        public const string SectionName = "DataProtectionRecovery";

        public bool Enabled { get; set; }

        public int BackupCadenceMinutes { get; set; } = 30;

        public int MaximumRpoMinutes { get; set; } = 60;

        public int TargetRtoMinutes { get; set; } = 240;

        public int PitrWindowDays { get; set; } = 30;

        public int AllRecoveryPointsHours { get; set; } = 48;

        public int DailyRecoveryPointsDays { get; set; } = 30;

        public int MonthlyRecoveryPointsMonths { get; set; } = 12;

        public int RestoreDrillIntervalDays { get; set; } = 30;

        public int ProtectedCutoffWarningMinutes { get; set; } = 45;

        public int ProtectedCutoffCriticalMinutes { get; set; } = 60;

        public long RecordChangeThreshold { get; set; } = 1000;

        public long ByteChangeThreshold { get; set; } = 104857600;

        public int MaximumCoalescingMinutes { get; set; } = 15;

        public string BackupProvider { get; set; } = string.Empty;

        public string IndependentStore { get; set; } = string.Empty;

        public string KeyReference { get; set; } = string.Empty;

        public string NotificationTarget { get; set; } = string.Empty;

        public string RecoveryHostResourceId { get; set; } = string.Empty;

        public static bool IsValid(DataProtectionRecoveryOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var targetsAreValid = options.BackupCadenceMinutes is >= 1 and <= 60 &&
                options.MaximumRpoMinutes is >= 1 and <= 60 &&
                options.TargetRtoMinutes is >= 1 and <= 1440 &&
                options.PitrWindowDays is >= 1 and <= 365 &&
                options.AllRecoveryPointsHours is >= 1 and <= 168 &&
                options.DailyRecoveryPointsDays is >= 1 and <= 365 &&
                options.MonthlyRecoveryPointsMonths is >= 1 and <= 120 &&
                options.RestoreDrillIntervalDays is >= 1 and <= 365 &&
                options.ProtectedCutoffWarningMinutes >= 1 &&
                options.ProtectedCutoffCriticalMinutes <= options.MaximumRpoMinutes &&
                options.ProtectedCutoffWarningMinutes < options.ProtectedCutoffCriticalMinutes &&
                options.RecordChangeThreshold > 0 &&
                options.ByteChangeThreshold > 0 &&
                options.MaximumCoalescingMinutes is >= 1 and <= 60;
            var requiredReferencesConfigured = !options.Enabled ||
                (!string.IsNullOrWhiteSpace(options.BackupProvider) &&
                 !string.IsNullOrWhiteSpace(options.IndependentStore) &&
                 !string.IsNullOrWhiteSpace(options.KeyReference) &&
                 !string.IsNullOrWhiteSpace(options.NotificationTarget) &&
                 !string.IsNullOrWhiteSpace(options.RecoveryHostResourceId));
            return targetsAreValid && requiredReferencesConfigured;
        }
    }
}
