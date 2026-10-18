using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    [TestFixture]
    internal sealed class RecoveryCompletionCoordinatorTests
    {
        [Test]
        public async Task CompleteAsync_WithSettledReconciledGeneration_ActivatesAfterOrderedStages()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();

            var result = await scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);

            scenario.Calls.Should().Equal(
                RecoveryExecutionStage.ReplayAcceptedPending,
                RecoveryExecutionStage.RebuildProjections,
                RecoveryExecutionStage.ReconcileApis);
            scenario.Replay.LastContext.SuppressNotifications.Should().BeTrue();
            scenario.Replay.LastContext.SuppressExternalSideEffects.Should().BeTrue();
            result.State.Should().Be(RecoveryExecutionState.Activated);
            result.Activated.Should().BeTrue();
            result.VisibleGenerationId.Should().Be("generation-8");
            result.RollbackEligible.Should().BeTrue();
        }

        [TestCase(RecoveryExecutionStage.ReplayAcceptedPending)]
        [TestCase(RecoveryExecutionStage.RebuildProjections)]
        [TestCase(RecoveryExecutionStage.ReconcileApis)]
        public async Task CompleteAsync_WhenStageFails_OldGenerationRemainsVisibleAndResumeCompletes(
            RecoveryExecutionStage failedStage)
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario { FailStageOnce = failedStage };

            await AssertFailureAsync("recovery-completion-stage-failed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));

            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            store.Journal.State.Should().Be(RecoveryExecutionState.Failed);
            store.Journal.RollbackEligible.Should().BeTrue();

            var result = await scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);
            result.State.Should().Be(RecoveryExecutionState.Activated);
            scenario.Guard.ActiveGenerationId.Should().Be("generation-8");
        }

        [Test]
        public async Task CompleteAsync_WhenReplayCheckpointCrashes_RetryHasZeroDuplicateFinancialEffect()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            store.FailCompletionCheckpointOnce = RecoveryExecutionStage.ReplayAcceptedPending;
            var scenario = new RecoveryCompletionScenario();

            await AssertFailureAsync("recovery-completion-stage-failed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));
            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");

            var result = await scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);

            result.State.Should().Be(RecoveryExecutionState.Activated);
            scenario.Replay.InvocationCount.Should().Be(2);
            scenario.FinancialEffectCount.Should().Be(3);
        }

        [Test]
        public async Task CompleteAsync_WhenReadyCheckpointCrashes_DoesNotAttemptActivationUntilResume()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            store.FailReadyCheckpointOnce = true;
            var scenario = new RecoveryCompletionScenario();

            await AssertFailureAsync("recovery-completion-stage-failed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));

            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            scenario.Activator.InvocationCount.Should().Be(0);

            var result = await scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);
            result.Activated.Should().BeTrue();
        }

        [Test]
        public async Task CompleteAsync_WhenCompareAndSwapFails_OldGenerationRemainsAndResumeCompletes()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            scenario.Activator.FailBeforeSwapOnce = true;

            await AssertFailureAsync("activation-failed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));
            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            store.Journal.RollbackEligible.Should().BeTrue();

            var result = await scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);
            result.Activated.Should().BeTrue();
        }

        [Test]
        public async Task CompleteAsync_WhenActivatedCheckpointCrashes_OnlyValidatedNewGenerationIsVisibleAndResumeRepairsJournal()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            store.FailActivatedCheckpointOnce = true;
            var scenario = new RecoveryCompletionScenario();

            await AssertFailureAsync("activation-checkpoint-failed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));
            scenario.Guard.ActiveGenerationId.Should().Be("generation-8");
            store.Journal.State.Should().Be(RecoveryExecutionState.ReadyToActivate);

            var result = await scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);
            result.State.Should().Be(RecoveryExecutionState.Activated);
            scenario.Activator.InvocationCount.Should().Be(2);
            scenario.Calls.Count.Should().Be(3);
        }

        [Test]
        public async Task CompleteAsync_WhenApiReconciliationFails_NeverActivates()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            scenario.Reconciliation.Passes = false;

            await AssertFailureAsync("reconciliation-failed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));

            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            scenario.Activator.InvocationCount.Should().Be(0);
            store.Journal.State.Should().Be(RecoveryExecutionState.Failed);
        }

        [Test]
        public async Task CompleteAsync_WithMismatchedReconciliationBinding_NeverActivates()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            scenario.Reconciliation.ReturnWrongBinding = true;

            await AssertFailureAsync("completion-evidence-mismatch", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));

            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            scenario.Activator.InvocationCount.Should().Be(0);
        }

        [TestCase("unsettled", "pending-replay-unsettled")]
        [TestCase("idempotency", "pending-replay-unsettled")]
        [TestCase("side-effects", "replay-side-effects-not-suppressed")]
        [TestCase("rebuild", "projection-rebuild-incomplete")]
        public async Task CompleteAsync_WithIncompleteReplayOrRebuildEvidence_NeverActivates(
            string defect,
            string expectedCode)
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            scenario.Replay.SettlesAll = defect != "unsettled";
            scenario.Replay.PreservesIdempotency = defect != "idempotency";
            scenario.Replay.SuppressesSideEffects = defect != "side-effects";
            scenario.Rebuild.CompletesRebuild = defect != "rebuild";

            await AssertFailureAsync(expectedCode, () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));

            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            scenario.Activator.InvocationCount.Should().Be(0);
            store.Journal.RollbackEligible.Should().BeTrue();
        }

        [Test]
        public async Task CompleteAsync_WhenCancelled_PersistsRollbackEligibleFailure()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            scenario.Replay.CancelOnce = true;

            await FluentActions.Awaiting(() => scenario.Coordinator(store).CompleteAsync(
                    RecoveryExecutionScenario.Request(),
                    CancellationToken.None))
                .Should().ThrowAsync<OperationCanceledException>();

            scenario.Guard.ActiveGenerationId.Should().Be("generation-7");
            store.Journal.State.Should().Be(RecoveryExecutionState.Failed);
            store.Journal.RollbackEligible.Should().BeTrue();
        }

        [Test]
        public async Task CompleteAsync_AfterActivation_IsIdempotentAndDoesNotRepeatWork()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            var request = RecoveryExecutionScenario.Request();

            await scenario.Coordinator(store).CompleteAsync(request, CancellationToken.None);
            var result = await scenario.Coordinator(store).CompleteAsync(request, CancellationToken.None);

            result.State.Should().Be(RecoveryExecutionState.Activated);
            scenario.Calls.Count.Should().Be(3);
            scenario.Activator.InvocationCount.Should().Be(1);
        }

        [Test]
        public async Task CompleteAsync_WithoutCompletedRestorePrefix_RejectsBeforeMutation()
        {
            var store = new InMemoryRecoveryExecutionJournalStore();
            var request = RecoveryExecutionScenario.Request();
            await store.CreateAsync(
                new RecoveryExecutionJournal(
                    request.OperationId,
                    request.Artifact,
                    request.Target,
                    request.Target.ActiveGenerationId,
                    RecoveryExecutionState.AwaitingReplayRebuild,
                    0,
                    Array.Empty<RecoveryExecutionStage>(),
                    true,
                    null),
                CancellationToken.None);
            var scenario = new RecoveryCompletionScenario();

            await AssertFailureAsync("restore-incomplete", () => scenario.Coordinator(store).CompleteAsync(
                request,
                CancellationToken.None));
            scenario.Calls.Should().BeEmpty();
            scenario.Activator.InvocationCount.Should().Be(0);
        }

        [Test]
        public async Task CompleteAsync_WhenActiveGenerationChangedBeforeCompletion_RejectsWithoutMutation()
        {
            var store = await RecoveryCompletionScenario.RestoredStoreAsync();
            var scenario = new RecoveryCompletionScenario();
            scenario.Guard.ActiveGenerationId = "generation-other";

            await AssertFailureAsync("active-generation-changed", () => scenario.Coordinator(store).CompleteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));
            scenario.Calls.Should().BeEmpty();
            scenario.Activator.InvocationCount.Should().Be(0);
        }

        private static async Task AssertFailureAsync(string code, Func<Task> action)
        {
            var exception = await FluentActions.Awaiting(action)
                .Should().ThrowAsync<RecoveryExecutionException>();
            exception.Which.Code.Should().Be(code);
        }
    }
}
