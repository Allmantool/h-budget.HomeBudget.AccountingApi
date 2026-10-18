using System;
using System.Collections.Generic;
using System.Linq;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    internal static class RecoveryPlanningScenario
    {
        public static readonly DateTimeOffset BaselineCutoff =
            new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

        public static readonly DateTimeOffset RequestedCutoff =
            new(2026, 9, 20, 10, 10, 0, TimeSpan.Zero);

        public static readonly Guid BaselineId =
            Guid.Parse("c31dd90f-e2e5-4565-b85d-f3d56ec305fa");

        public static RecoveryPlanningRequest Request(
            RecoveryPlanningMode mode = RecoveryPlanningMode.PointInTime,
            Guid? recoverySetId = null) => new(
                mode,
                recoverySetId,
                RequestedCutoff,
                "integration",
                "family-ledger",
                "generation-7",
                "2.8.0",
                "home-ledger-recovery-set/v1",
                new RecoveryTargetIdentity(
                    "restore-target-1",
                    "integration",
                    "family-ledger",
                    "generation-8",
                    "generation-7",
                    true,
                    true),
                true,
                true);

        public static RecoveryEvidenceInventory Inventory()
        {
            var baseline = Baseline();
            return new RecoveryEvidenceInventory([baseline], Chains(baseline));
        }

        public static VerifiedRecoverySetEvidence Baseline() => new(
            BaselineId,
            "integration",
            "family-ledger",
            "generation-7",
            "2.8.0",
            "home-ledger-recovery-set/v1",
            BaselineCutoff,
            true,
            true,
            true,
            true,
            true,
            Enum.GetValues<RecoveryComponent>()
                .Select(component => new RecoveryBaselineComponentEvidence(
                    component,
                    $"baseline/{component}",
                    BaselineCutoff,
                    100,
                    $"{component}:100",
                    RequiredCoverage(component),
                    true,
                    true))
                .ToArray());

        public static IReadOnlyCollection<RecoveryComponentChainEvidence> Chains(
            VerifiedRecoverySetEvidence baseline) =>
            Enum.GetValues<RecoveryComponent>()
                .Select(component => Chain(component, baseline))
                .ToArray();

        public static RecoveryComponentChainEvidence Chain(
            RecoveryComponent component,
            VerifiedRecoverySetEvidence baseline)
        {
            var resolved = RequestedCutoff.AddSeconds(-2);
            return new RecoveryComponentChainEvidence(
                component,
                BaselineCutoff,
                RequestedCutoff.AddHours(1),
                resolved,
                150,
                $"{component}:150",
                Precision(component),
                [Segment(component, baseline)]);
        }

        public static RecoveryHistorySegmentEvidence Segment(
            RecoveryComponent component,
            VerifiedRecoverySetEvidence baseline) => new(
                component,
                $"continuous/{component}/101-200",
                BaselineCutoff,
                RequestedCutoff.AddMinutes(5),
                100,
                200,
                $"{component}:200",
                RequiredCoverage(component),
                true,
                true,
                Mutations(component));

        public static RecoveryHistoryCoverage RequiredCoverage(RecoveryComponent component) =>
            component switch
            {
                RecoveryComponent.Sql =>
                    RecoveryHistoryCoverage.TransferRegistrations |
                    RecoveryHistoryCoverage.ProcessingState,
                RecoveryComponent.EventStore => RecoveryHistoryCoverage.PaymentEvents,
                RecoveryComponent.Mongo =>
                    RecoveryHistoryCoverage.Accounts |
                    RecoveryHistoryCoverage.Categories |
                    RecoveryHistoryCoverage.Contractors |
                    RecoveryHistoryCoverage.ReferenceUpdatesAndDeletes |
                    RecoveryHistoryCoverage.BusinessSettings |
                    RecoveryHistoryCoverage.SettingsUpdatesAndDeletes |
                    RecoveryHistoryCoverage.MigrationLineage,
                RecoveryComponent.Rates =>
                    RecoveryHistoryCoverage.Rates |
                    RecoveryHistoryCoverage.RateUpdatesAndDeletes,
                RecoveryComponent.PendingWork => RecoveryHistoryCoverage.AcceptedPendingCommands,
                _ => RecoveryHistoryCoverage.None
            };

        private static RecoveryMutationEvidence[] Mutations(RecoveryComponent component) =>
            component switch
            {
                RecoveryComponent.EventStore =>
                [Mutation("payment-committed", component, RecoveryMutationKind.PaymentEvent, 105)],
                RecoveryComponent.Mongo =>
                [
                    Mutation("category-updated", component, RecoveryMutationKind.ReferenceUpdate, 102),
                    Mutation("account-archived", component, RecoveryMutationKind.ReferenceDelete, 103),
                    Mutation("setting-updated", component, RecoveryMutationKind.SettingUpdate, 104),
                    Mutation("setting-deleted", component, RecoveryMutationKind.SettingDelete, 105),
                    Mutation(
                        "backdated-late-payment",
                        component,
                        RecoveryMutationKind.ReferenceUpdate,
                        111,
                        RequestedCutoff.AddSeconds(30),
                        new DateOnly(2020, 1, 1))
                ],
                RecoveryComponent.Rates =>
                [
                    Mutation("rate-updated", component, RecoveryMutationKind.RateUpdate, 102),
                    Mutation("rate-deleted", component, RecoveryMutationKind.RateDelete, 103)
                ],
                RecoveryComponent.PendingWork =>
                [Mutation("accepted-command-1", component, RecoveryMutationKind.AcceptedPendingCommand, 105)],
                _ => Array.Empty<RecoveryMutationEvidence>()
            };

        private static RecoveryMutationEvidence Mutation(
            string id,
            RecoveryComponent component,
            RecoveryMutationKind kind,
            long sequence,
            DateTimeOffset? durableTimestamp = null,
            DateOnly? businessDate = null) => new(
                id,
                component,
                kind,
                durableTimestamp ?? BaselineCutoff.AddMinutes(sequence - 100),
                sequence,
                businessDate);

        private static string Precision(RecoveryComponent component) => component switch
        {
            RecoveryComponent.Sql => "sql-stopat",
            RecoveryComponent.EventStore => "commit-position",
            RecoveryComponent.Mongo => "mongodb-timestamp",
            RecoveryComponent.Rates => "rates-commit-sequence",
            RecoveryComponent.PendingWork => "acceptance-sequence",
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
    }
}
