using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    [TestFixture]
    internal sealed class RecoveryExecutionCoordinatorTests
    {
        [Test]
        public async Task ExecuteAsync_WithVerifiedIsolatedTarget_RunsStagesInOrderAndStopsBeforeReplay()
        {
            var calls = new List<RecoveryExecutionStage>();
            var store = new InMemoryRecoveryExecutionJournalStore();
            var guard = new FakeRecoveryActiveGenerationGuard();
            var coordinator = Create(store, guard, calls);

            var result = await coordinator.ExecuteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);

            calls.Should().Equal(RecoveryExecutionScenario.OrderedStages);
            result.State.Should().Be(RecoveryExecutionState.AwaitingReplayRebuild);
            result.CompletedStages.Should().Equal(RecoveryExecutionScenario.OrderedStages);
            result.RollbackEligible.Should().BeTrue();
            result.ActivationAllowed.Should().BeFalse();
            guard.ActiveGenerationId.Should().Be("generation-7");
        }

        [TestCase(RecoveryExecutionStage.PrepareTarget)]
        [TestCase(RecoveryExecutionStage.RestoreSql)]
        [TestCase(RecoveryExecutionStage.RestoreEventStore)]
        [TestCase(RecoveryExecutionStage.RestoreMongo)]
        [TestCase(RecoveryExecutionStage.RestoreRates)]
        [TestCase(RecoveryExecutionStage.ReconstructMessaging)]
        [TestCase(RecoveryExecutionStage.ReconstructProcessingState)]
        public async Task ExecuteAsync_WhenStageFails_PersistsCheckpointAndResumesWithoutRepeatingCompletedStages(
            RecoveryExecutionStage failAt)
        {
            var calls = new List<RecoveryExecutionStage>();
            var store = new InMemoryRecoveryExecutionJournalStore();
            var guard = new FakeRecoveryActiveGenerationGuard();
            var adapters = RecoveryExecutionScenario.Adapters(calls, failAt);
            var coordinator = new RecoveryExecutionCoordinator(store, guard, adapters);

            await AssertFailureAsync("restore-stage-failed", () => coordinator.ExecuteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None));
            store.Journal.State.Should().Be(RecoveryExecutionState.Failed);
            store.Journal.RollbackEligible.Should().BeTrue();
            guard.ActiveGenerationId.Should().Be("generation-7");

            var completedBeforeResume = store.Journal.CompletedStages.ToArray();
            var result = await new RecoveryExecutionCoordinator(store, guard, adapters).ExecuteAsync(
                RecoveryExecutionScenario.Request(),
                CancellationToken.None);

            result.State.Should().Be(RecoveryExecutionState.AwaitingReplayRebuild);
            foreach (var completed in completedBeforeResume)
            {
                calls.Count(item => item == completed).Should().Be(1);
            }

            calls.Count(item => item == failAt).Should().Be(2);
        }

        [Test]
        public async Task ExecuteAsync_WithRunningEventStoreTarget_RejectsBeforeJournalOrAdapters()
        {
            var request = RecoveryExecutionScenario.Request();
            request = request with { Target = request.Target with { IsEventStoreStopped = false } };
            var calls = new List<RecoveryExecutionStage>();
            var store = new InMemoryRecoveryExecutionJournalStore();

            await AssertFailureAsync("eventstore-target-running", () =>
                Create(store, new FakeRecoveryActiveGenerationGuard(), calls).ExecuteAsync(
                    request,
                    CancellationToken.None));
            store.Journal.Should().BeNull();
            calls.Should().BeEmpty();
        }

        [TestCase("TargetId")]
        [TestCase("GenerationId")]
        [TestCase("ActiveGeneration")]
        public async Task ExecuteAsync_WithTargetIdentityMismatch_RejectsBeforeMutation(string mismatch)
        {
            var request = RecoveryExecutionScenario.Request();
            request = mismatch switch
            {
                "TargetId" => request with { Target = request.Target with { TargetId = "other-target" } },
                "GenerationId" => request with { Target = request.Target with { GenerationId = "generation-9" } },
                _ => request with { Target = request.Target with { ActiveGenerationId = "generation-6" } }
            };

            await AssertFailureAsync("target-binding-mismatch", () => Create(
                    new InMemoryRecoveryExecutionJournalStore(),
                    new FakeRecoveryActiveGenerationGuard(),
                    []).ExecuteAsync(request, CancellationToken.None));
        }

        [TestCase("topic")]
        [TestCase("group")]
        [TestCase("subscription")]
        public async Task ExecuteAsync_WithNonGenerationScopedMessaging_Rejects(string resource)
        {
            var request = RecoveryExecutionScenario.Request();
            var wrong = request.Target.TopicNamespace with { GenerationId = "generation-7" };
            request = resource switch
            {
                "topic" => request with { Target = request.Target with { TopicNamespace = wrong } },
                "group" => request with { Target = request.Target with { ConsumerGroup = wrong } },
                _ => request with { Target = request.Target with { Subscription = wrong } }
            };

            await AssertFailureAsync("messaging-target-not-generation-scoped", () => Create(
                    new InMemoryRecoveryExecutionJournalStore(),
                    new FakeRecoveryActiveGenerationGuard(),
                    []).ExecuteAsync(request, CancellationToken.None));
        }

        [TestCase(false, "2.8.0", "checksum-unverified")]
        [TestCase(true, "2.7.9", "version-incompatible")]
        public async Task ExecuteAsync_WithUnverifiedOrIncompatibleManifest_Rejects(
            bool checksumVerified,
            string applicationVersion,
            string code)
        {
            var request = RecoveryExecutionScenario.Request();
            request = request with
            {
                Artifact = request.Artifact with
                {
                    IsChecksumVerified = checksumVerified,
                    ApplicationVersion = applicationVersion
                }
            };

            await AssertFailureAsync(code, () => Create(
                    new InMemoryRecoveryExecutionJournalStore(),
                    new FakeRecoveryActiveGenerationGuard(),
                    []).ExecuteAsync(request, CancellationToken.None));
        }

        [Test]
        public async Task ExecuteAsync_WhenResumeBindingChanges_RejectsChangedManifest()
        {
            var calls = new List<RecoveryExecutionStage>();
            var store = new InMemoryRecoveryExecutionJournalStore();
            var guard = new FakeRecoveryActiveGenerationGuard();
            var coordinator = new RecoveryExecutionCoordinator(
                store,
                guard,
                RecoveryExecutionScenario.Adapters(calls, RecoveryExecutionStage.RestoreSql));
            var request = RecoveryExecutionScenario.Request();
            await AssertFailureAsync("restore-stage-failed", () =>
                coordinator.ExecuteAsync(request, CancellationToken.None));

            var changed = request with
            {
                Artifact = request.Artifact with { ManifestSha256 = new string('b', 64) }
            };
            await AssertFailureAsync("resume-binding-conflict", () =>
                coordinator.ExecuteAsync(changed, CancellationToken.None));
        }

        [Test]
        public async Task ExecuteAsync_WhenProviderCompletionEvidenceDoesNotMatchManifest_FailsClosed()
        {
            var calls = new List<RecoveryExecutionStage>();
            var store = new InMemoryRecoveryExecutionJournalStore();
            var adapters = RecoveryExecutionScenario.Adapters(calls)
                .Select(adapter => adapter.Stage == RecoveryExecutionStage.RestoreSql
                    ? new MismatchedEvidenceAdapter(adapter)
                    : adapter)
                .ToArray();

            await AssertFailureAsync("restore-evidence-mismatch", () =>
                new RecoveryExecutionCoordinator(store, new FakeRecoveryActiveGenerationGuard(), adapters)
                    .ExecuteAsync(RecoveryExecutionScenario.Request(), CancellationToken.None));

            store.Journal.State.Should().Be(RecoveryExecutionState.Failed);
            store.Journal.CompletedStages.Should().Equal(RecoveryExecutionStage.PrepareTarget);
        }

        [Test]
        public async Task ExecuteAsync_WhenAdapterMutatesActiveGeneration_FailsAndRemainsRollbackEligible()
        {
            var calls = new List<RecoveryExecutionStage>();
            var store = new InMemoryRecoveryExecutionJournalStore();
            var guard = new FakeRecoveryActiveGenerationGuard();
            var adapters = RecoveryExecutionScenario.Adapters(
                calls,
                afterStage: stage =>
                {
                    if (stage == RecoveryExecutionStage.RestoreSql)
                    {
                        guard.ActiveGenerationId = "generation-8";
                    }
                });

            await AssertFailureAsync("active-generation-changed", () =>
                new RecoveryExecutionCoordinator(store, guard, adapters).ExecuteAsync(
                    RecoveryExecutionScenario.Request(),
                    CancellationToken.None));

            store.Journal.State.Should().Be(RecoveryExecutionState.Failed);
            store.Journal.RollbackEligible.Should().BeTrue();
            calls.Should().Equal(
                RecoveryExecutionStage.PrepareTarget,
                RecoveryExecutionStage.RestoreSql);
        }

        private static RecoveryExecutionCoordinator Create(
            InMemoryRecoveryExecutionJournalStore store,
            FakeRecoveryActiveGenerationGuard guard,
            IList<RecoveryExecutionStage> calls) => new(
                store,
                guard,
                RecoveryExecutionScenario.Adapters(calls));

        private static async Task AssertFailureAsync(string code, Func<Task> action)
        {
            var exception = await FluentActions.Awaiting(action)
                .Should().ThrowAsync<RecoveryExecutionException>();
            exception.Which.Code.Should().Be(code);
        }

        private sealed class MismatchedEvidenceAdapter : IRecoveryRestoreStageAdapter
        {
            private readonly IRecoveryRestoreStageAdapter _inner;

            public MismatchedEvidenceAdapter(IRecoveryRestoreStageAdapter inner)
            {
                _inner = inner;
            }

            public RecoveryExecutionStage Stage => _inner.Stage;

            public async Task<RecoveryStageCompletionEvidence> ExecuteAsync(
                RecoveryRestoreContext context,
                CancellationToken cancellationToken)
            {
                var evidence = await _inner.ExecuteAsync(context, cancellationToken);
                return evidence with { ManifestSha256 = new string('f', 64) };
            }
        }
    }
}
