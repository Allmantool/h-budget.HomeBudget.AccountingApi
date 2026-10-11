using System;
using System.Linq;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    [TestFixture]
    internal sealed class RecoveryPlanSelectorTests
    {
        private readonly RecoveryPlanSelector _selector = new();

        [Test]
        public void Select_SavedPoint_ReportsActualCutoffPrecisionAndAllComponentWatermarks()
        {
            var request = RecoveryPlanningScenario.Request(
                RecoveryPlanningMode.SavedPoint,
                RecoveryPlanningScenario.BaselineId);

            var plan = _selector.Select(request, RecoveryPlanningScenario.Inventory());

            plan.RequestedCutoffUtc.Should().Be(RecoveryPlanningScenario.RequestedCutoff);
            plan.ActualCutoffUtc.Should().Be(RecoveryPlanningScenario.BaselineCutoff);
            plan.Precision.Should().Be("saved-recovery-set");
            plan.ComponentWatermarks.Should().HaveCount(5);
            plan.SelectedSegments.Should().BeEmpty();
        }

        [Test]
        public void Select_BetweenSavedPoints_UsesPrecedingBaselineAndLatestCommonCutoff()
        {
            var inventory = RecoveryPlanningScenario.Inventory();
            var later = RecoveryPlanningScenario.Baseline() with
            {
                RecoverySetId = Guid.NewGuid(),
                CommonCutoffUtc = RecoveryPlanningScenario.RequestedCutoff.AddMinutes(10)
            };
            inventory = inventory with { RecoverySets = [.. inventory.RecoverySets, later] };

            var plan = _selector.Select(RecoveryPlanningScenario.Request(), inventory);

            plan.BaselineRecoverySetId.Should().Be(RecoveryPlanningScenario.BaselineId);
            plan.ActualCutoffUtc.Should().Be(RecoveryPlanningScenario.RequestedCutoff.AddSeconds(-2));
            plan.ActualCutoffUtc.Offset.Should().Be(TimeSpan.Zero);
            plan.Precision.Should().Be("coordinated-continuous-chain");
            plan.ComponentWatermarks.Should().OnlyContain(item =>
                item.CutoffUtc == plan.ActualCutoffUtc && item.DurableSequence == 150);
        }

        [Test]
        public void Select_WhenComponentsDoNotResolveOneCommonCutoff_Rejects()
        {
            var inventory = MutateChain(RecoveryComponent.Sql, chain => chain with
            {
                ResolvedCutoffUtc = chain.ResolvedCutoffUtc.AddSeconds(-1)
            });

            AssertFailure("component-cutoff-mismatch", inventory);
        }

        [Test]
        public void Select_UsesDurableOrderingAndExcludesLaterBackdatedMutation()
        {
            var plan = _selector.Select(
                RecoveryPlanningScenario.Request(),
                RecoveryPlanningScenario.Inventory());

            plan.MembershipBasis.Should().Be("durable-commit-and-acceptance-order");
            plan.IncludedMutations.Should().NotContain(item => item.MutationId == "backdated-late-payment");
            plan.IncludedMutations.Should().Contain(item => item.MutationId == "payment-committed");
        }

        [Test]
        public void Select_IncludesReferenceSettingsRateHistoryAndAcceptedPendingWork()
        {
            var plan = _selector.Select(
                RecoveryPlanningScenario.Request(),
                RecoveryPlanningScenario.Inventory());

            plan.IncludedMutations.Select(item => item.Kind).Should().BeSupersetOf(
            [
                RecoveryMutationKind.ReferenceUpdate,
                RecoveryMutationKind.ReferenceDelete,
                RecoveryMutationKind.SettingUpdate,
                RecoveryMutationKind.SettingDelete,
                RecoveryMutationKind.RateUpdate,
                RecoveryMutationKind.RateDelete,
                RecoveryMutationKind.AcceptedPendingCommand
            ]);
        }

        [TestCase(RecoveryComponent.Mongo, RecoveryHistoryCoverage.ReferenceUpdatesAndDeletes)]
        [TestCase(RecoveryComponent.Mongo, RecoveryHistoryCoverage.SettingsUpdatesAndDeletes)]
        [TestCase(RecoveryComponent.Rates, RecoveryHistoryCoverage.RateUpdatesAndDeletes)]
        public void Select_WhenRequiredHistoricalCoverageIsMissing_Rejects(
            RecoveryComponent component,
            RecoveryHistoryCoverage missing)
        {
            var inventory = MutateChain(component, chain => chain with
            {
                Segments = chain.Segments
                    .Select(segment => segment with { Coverage = segment.Coverage & ~missing })
                    .ToArray()
            });

            AssertFailure("history-coverage-incomplete", inventory);
        }

        [Test]
        public void Select_WhenContinuousChainHasGap_RejectsRequestedPoint()
        {
            var inventory = MutateChain(RecoveryComponent.Sql, chain => chain with
            {
                Segments = chain.Segments.Select(segment => segment with
                {
                    FromExclusiveUtc = segment.FromExclusiveUtc.AddMinutes(1),
                    FromSequenceExclusive = segment.FromSequenceExclusive + 1
                }).ToArray()
            });

            AssertFailure("continuous-history-gap", inventory);
        }

        [TestCase(true, false, "version-incompatible")]
        [TestCase(false, true, "checksum-unverified")]
        public void Select_WhenSelectedSegmentIsUntrusted_Rejects(
            bool checksumVerified,
            bool versionCompatible,
            string code)
        {
            var inventory = MutateChain(RecoveryComponent.EventStore, chain => chain with
            {
                Segments = chain.Segments.Select(segment => segment with
                {
                    IsChecksumVerified = checksumVerified,
                    IsVersionCompatible = versionCompatible
                }).ToArray()
            });

            AssertFailure(code, inventory);
        }

        [Test]
        public void Select_WhenRequestedTimeIsOutsideVerifiedInterval_RejectsInsteadOfFallingBack()
        {
            var inventory = MutateChain(RecoveryComponent.Mongo, chain => chain with
            {
                RecoverableToUtc = RecoveryPlanningScenario.RequestedCutoff.AddSeconds(-1)
            });

            AssertFailure("cutoff-unavailable", inventory);
        }

        [TestCase(false, true, "notification-suppression-required")]
        [TestCase(true, false, "external-side-effect-suppression-required")]
        public void Select_WithoutReplaySideEffectSuppression_Rejects(
            bool suppressNotifications,
            bool suppressExternalSideEffects,
            string code)
        {
            var request = RecoveryPlanningScenario.Request() with
            {
                SuppressNotifications = suppressNotifications,
                SuppressExternalSideEffects = suppressExternalSideEffects
            };

            AssertFailure(code, RecoveryPlanningScenario.Inventory(), request);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void Select_WithoutIsolatedEmptyInactiveTarget_Rejects(bool isolated, bool empty)
        {
            var request = RecoveryPlanningScenario.Request();
            request = request with { Target = request.Target with { IsIsolated = isolated, IsEmpty = empty } };

            AssertFailure("target-not-isolated", RecoveryPlanningScenario.Inventory(), request);
        }

        [Test]
        public void Select_WhenTargetIsActiveGeneration_RejectsMixedActivation()
        {
            var request = RecoveryPlanningScenario.Request();
            request = request with
            {
                Target = request.Target with { GenerationId = request.Target.ActiveGenerationId }
            };

            AssertFailure("target-generation-active", RecoveryPlanningScenario.Inventory(), request);
        }

        [Test]
        public void Select_ProducesPlanningOnlyResultWithNoActivationAuthority()
        {
            var plan = _selector.Select(
                RecoveryPlanningScenario.Request(),
                RecoveryPlanningScenario.Inventory());

            plan.ActivationAllowed.Should().BeFalse();
            plan.Target.GenerationId.Should().NotBe(plan.Target.ActiveGenerationId);
            plan.SuppressNotifications.Should().BeTrue();
            plan.SuppressExternalSideEffects.Should().BeTrue();
            plan.PlanFingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        private static RecoveryEvidenceInventory MutateChain(
            RecoveryComponent component,
            Func<RecoveryComponentChainEvidence, RecoveryComponentChainEvidence> mutate)
        {
            var inventory = RecoveryPlanningScenario.Inventory();
            return inventory with
            {
                ComponentChains = inventory.ComponentChains
                    .Select(chain => chain.Component == component ? mutate(chain) : chain)
                    .ToArray()
            };
        }

        private void AssertFailure(
            string code,
            RecoveryEvidenceInventory inventory,
            RecoveryPlanningRequest request = null)
        {
            var action = () => _selector.Select(request ?? RecoveryPlanningScenario.Request(), inventory);
            action.Should().Throw<RecoveryPlanningException>().Which.Code.Should().Be(code);
        }
    }
}
