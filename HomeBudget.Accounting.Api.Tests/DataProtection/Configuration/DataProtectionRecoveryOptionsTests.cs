#nullable enable

using FluentAssertions;

using HomeBudget.Accounting.Api.Configuration;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Configuration
{
    [TestFixture]
    internal sealed class DataProtectionRecoveryOptionsTests
    {
        [Test]
        public void Defaults_MatchInitialRecoveryTargetsAndRemainDisabled()
        {
            var options = new DataProtectionRecoveryOptions();

            options.Enabled.Should().BeFalse();
            options.BackupCadenceMinutes.Should().Be(30);
            options.MaximumRpoMinutes.Should().Be(60);
            options.TargetRtoMinutes.Should().Be(240);
            options.PitrWindowDays.Should().Be(30);
            options.AllRecoveryPointsHours.Should().Be(48);
            options.DailyRecoveryPointsDays.Should().Be(30);
            options.MonthlyRecoveryPointsMonths.Should().Be(12);
            options.RestoreDrillIntervalDays.Should().Be(30);
            options.ProtectedCutoffWarningMinutes.Should().Be(45);
            options.ProtectedCutoffCriticalMinutes.Should().Be(60);
        }

        [Test]
        public void Evaluate_WithMissingProductionPrerequisites_ReturnsBoundedIdentifiers()
        {
            var result = DataProtectionRecoveryReadiness.Evaluate(
                new DataProtectionRecoveryOptions(),
                DataProtectionRuntimeReadinessEvidence.Unavailable);

            result.Should().BeEquivalentTo(
                [
                    "data-protection-disabled",
                    "backup-provider-not-configured",
                    "independent-store-not-configured",
                    "key-reference-not-configured",
                    "notification-target-not-configured",
                    "recovery-host-not-configured"
                ],
                options => options.WithStrictOrdering());
        }

        [Test]
        public void Evaluate_WithAllReferencesConfiguredButNoValidatedRuntime_ReturnsBlockedEvidence()
        {
            var options = Configured();

            DataProtectionRecoveryReadiness.Evaluate(
                    options,
                    DataProtectionRuntimeReadinessEvidence.Unavailable)
                .Should()
                .BeEquivalentTo(
                    [
                        "backup-provider-not-validated",
                        "independent-store-not-validated",
                        "key-reference-not-validated",
                        "notification-target-not-validated",
                        "recovery-host-not-validated"
                    ],
                    resultOptions => resultOptions.WithStrictOrdering());
            DataProtectionRecoveryOptions.IsValid(options).Should().BeTrue();
        }

        [Test]
        public void Evaluate_WithConfiguredReferencesAndValidatedRuntime_ReturnsReady()
        {
            var options = Configured();

            DataProtectionRecoveryReadiness.Evaluate(
                    options,
                    EvidenceFor(options))
                .Should()
                .BeEmpty();
        }

        [Test]
        public void Evaluate_WhenValidatedRuntimeBelongsToDifferentReferences_ReturnsBlockedEvidence()
        {
            var options = Configured();
            var staleEvidence = EvidenceFor(options);
            options.IndependentStore = "s3://home-ledger-recovery/replacement";

            DataProtectionRecoveryReadiness.Evaluate(options, staleEvidence)
                .Should()
                .ContainSingle()
                .Which.Should().Be("independent-store-not-validated");
        }

        [Test]
        public void IsValid_WithUnsafeThresholdOrdering_RejectsConfiguration()
        {
            var options = Configured();
            options.ProtectedCutoffWarningMinutes = options.ProtectedCutoffCriticalMinutes;

            DataProtectionRecoveryOptions.IsValid(options).Should().BeFalse();
        }

        private static DataProtectionRecoveryOptions Configured() => new()
        {
            Enabled = true,
            BackupProvider = "sql-eventstore-mongo-coordinated-v1",
            IndependentStore = "s3://home-ledger-recovery/production",
            KeyReference = "kms://home-ledger-recovery-key",
            NotificationTarget = "alerts://home-ledger-operations",
            RecoveryHostResourceId = "host-resource/recovery-01"
        };

        private static DataProtectionRuntimeReadinessEvidence EvidenceFor(DataProtectionRecoveryOptions options)
        {
            return new DataProtectionRuntimeReadinessEvidence(
                options.BackupProvider,
                options.IndependentStore,
                options.KeyReference,
                options.NotificationTarget,
                options.RecoveryHostResourceId);
        }
    }
}
