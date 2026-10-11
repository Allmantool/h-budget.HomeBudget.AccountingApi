#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection.Reset;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Reset
{
    [TestFixture]
    internal sealed class LedgerResetCoordinatorTests
    {
        [Test]
        public async Task ExecuteAsync_WhenMandatoryBackupIsNotVerified_BlocksBeforeAnyMutation()
        {
            var scenario = ResetScenario.Create();
            scenario.Backup.Evidence = scenario.Backup.Evidence with { IsIndependentlyProtected = false };

            var execute = async () => await scenario.Coordinator.ExecuteAsync(
                scenario.Request,
                CancellationToken.None);

            await execute.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "prebackup-not-protected");
            scenario.Adapters.Should().OnlyContain(adapter => adapter.ExecutionCount == 0);
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.CurrentGenerationId);
        }

        [Test]
        public async Task ExecuteAsync_WithChangedPlanFingerprint_RejectsBeforeBackupOrMutation()
        {
            var scenario = ResetScenario.Create();
            var changed = scenario.Plan with { PlanFingerprint = new string('a', 64) };

            var execute = async () => await scenario.Coordinator.ExecuteAsync(
                new LedgerResetRequest(changed),
                CancellationToken.None);

            await execute.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "plan-fingerprint-mismatch");
            scenario.Backup.ExecutionCount.Should().Be(0);
            scenario.Adapters.Should().OnlyContain(adapter => adapter.ExecutionCount == 0);
        }

        [Test]
        public async Task ExecuteAsync_WhenPristineVerificationFails_NeverActivatesNewGeneration()
        {
            var scenario = ResetScenario.Create();
            scenario.Adapter(LedgerResetStage.VerifyPristine).Evidence =
                scenario.Adapter(LedgerResetStage.VerifyPristine).Evidence with
                {
                    RemainingBusinessEntityCounts = new Dictionary<string, long>
                    {
                        ["accounts"] = 1
                    }
                };

            var execute = async () => await scenario.Coordinator.ExecuteAsync(
                scenario.Request,
                CancellationToken.None);

            await execute.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "target-not-pristine");
            scenario.Generation.ActivationAttempts.Should().Be(0);
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.CurrentGenerationId);
        }

        [Test]
        public async Task ExecuteAsync_WithSharedResourceDestructionEvidence_FailsClosed()
        {
            var scenario = ResetScenario.Create();
            scenario.Adapter(LedgerResetStage.IsolateMessaging).Evidence =
                scenario.Adapter(LedgerResetStage.IsolateMessaging).Evidence with
                {
                    AffectsOnlySelectedLedger = false
                };

            var execute = async () => await scenario.Coordinator.ExecuteAsync(
                scenario.Request,
                CancellationToken.None);

            await execute.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "shared-scope-violation");
            scenario.Generation.ActivationAttempts.Should().Be(0);
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.CurrentGenerationId);
        }

        [Test]
        public async Task ExecuteAsync_AfterCrash_ResumesCompletedStagesWithoutRepeatingPrebackup()
        {
            var scenario = ResetScenario.Create();
            var failing = scenario.Adapter(LedgerResetStage.IsolateEventStore);
            failing.Failure = new InvalidOperationException("injected crash boundary");

            var first = async () => await scenario.Coordinator.ExecuteAsync(
                scenario.Request,
                CancellationToken.None);
            await first.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "reset-stage-failed");
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.CurrentGenerationId);

            failing.Failure = null;
            var resumed = await scenario.Coordinator.ExecuteAsync(scenario.Request, CancellationToken.None);

            resumed.State.Should().Be(LedgerResetState.Succeeded);
            resumed.ReimportAllowed.Should().BeTrue();
            scenario.Backup.ExecutionCount.Should().Be(1);
            scenario.Adapter(LedgerResetStage.DisposePendingWork).ExecutionCount.Should().Be(1);
            failing.ExecutionCount.Should().Be(2);
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.NewGenerationId);
        }

        [Test]
        public async Task ExecuteAsync_WhenActivationResponseIsLost_ResumeRecognizesSameOperationActivation()
        {
            var scenario = ResetScenario.Create();
            scenario.Generation.ThrowAfterFirstActivation = true;

            var first = async () => await scenario.Coordinator.ExecuteAsync(
                scenario.Request,
                CancellationToken.None);
            await first.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "activation-outcome-unknown");
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.NewGenerationId);

            var resumed = await scenario.Coordinator.ExecuteAsync(scenario.Request, CancellationToken.None);

            resumed.State.Should().Be(LedgerResetState.Succeeded);
            resumed.ReimportAllowed.Should().BeTrue();
            resumed.ActiveGenerationId.Should().Be(scenario.Plan.NewGenerationId);
            scenario.Generation.ActivationAttempts.Should().Be(1);
        }

        [Test]
        public async Task ExecuteAsync_WhenActiveGenerationChangedExternally_RejectsResume()
        {
            var scenario = ResetScenario.Create();
            scenario.Generation.ActiveGenerationId = "unrelated-generation";

            var execute = async () => await scenario.Coordinator.ExecuteAsync(
                scenario.Request,
                CancellationToken.None);

            await execute.Should().ThrowAsync<LedgerResetException>()
                .Where(error => error.Code == "active-generation-changed");
            scenario.Backup.ExecutionCount.Should().Be(0);
        }

        [Test]
        public async Task ExecuteAsync_WithValidEvidence_OrdersIsolationBeforePristineAndActivation()
        {
            var scenario = ResetScenario.Create();

            var result = await scenario.Coordinator.ExecuteAsync(scenario.Request, CancellationToken.None);

            result.State.Should().Be(LedgerResetState.Succeeded);
            result.ReimportAllowed.Should().BeTrue();
            result.RollbackEligible.Should().BeTrue();
            scenario.ExecutedStages.Should().Equal(LedgerResetCoordinator.OrderedStages);
            scenario.Generation.ActivationObservedStageCount.Should().Be(LedgerResetCoordinator.OrderedStages.Count);
            scenario.Generation.ActiveGenerationId.Should().Be(scenario.Plan.NewGenerationId);
        }

        private sealed class ResetScenario
        {
            private ResetScenario(
                LedgerResetPlan plan,
                FakeBackupGate backup,
                FakeJournalStore journal,
                FakeGenerationGuard generation,
                IReadOnlyList<FakeStageAdapter> adapters,
                List<LedgerResetStage> executedStages)
            {
                Plan = plan;
                Request = new LedgerResetRequest(plan);
                Backup = backup;
                Journal = journal;
                Generation = generation;
                Adapters = adapters;
                ExecutedStages = executedStages;
                Coordinator = new LedgerResetCoordinator(journal, backup, generation, adapters);
            }

            internal LedgerResetPlan Plan { get; }
            internal LedgerResetRequest Request { get; }
            internal FakeBackupGate Backup { get; }
            internal FakeJournalStore Journal { get; }
            internal FakeGenerationGuard Generation { get; }
            internal IReadOnlyList<FakeStageAdapter> Adapters { get; }
            internal List<LedgerResetStage> ExecutedStages { get; }
            internal LedgerResetCoordinator Coordinator { get; }

            internal static ResetScenario Create()
            {
                var operationId = Guid.NewGuid();
                var recoverySetId = Guid.NewGuid();
                var executed = new List<LedgerResetStage>();
                var counts = new Dictionary<string, long>
                {
                    ["accounts"] = 3,
                    ["operations"] = 50,
                    ["categories"] = 8,
                    ["contractors"] = 4
                };
                var resources = new[]
                {
                    new LedgerResetResource(
                        "sql-business",
                        "sql",
                        IsShared: true,
                        PreserveUnrelatedScopes: true,
                        OwnershipProofSha256: Hash("sql-business")),
                    new LedgerResetResource(
                        "eventstore-ledger-namespace",
                        "eventstore",
                        IsShared: true,
                        PreserveUnrelatedScopes: true,
                        OwnershipProofSha256: Hash("eventstore-ledger-namespace")),
                    new LedgerResetResource(
                        "mongo-ledger-scope",
                        "mongo",
                        IsShared: true,
                        PreserveUnrelatedScopes: true,
                        OwnershipProofSha256: Hash("mongo-ledger-scope")),
                    new LedgerResetResource(
                        "kafka-generation-resources",
                        "kafka",
                        IsShared: true,
                        PreserveUnrelatedScopes: true,
                        OwnershipProofSha256: Hash("kafka-generation-resources"))
                };
                var draft = new LedgerResetPlan(
                    operationId,
                    "disposable-environment",
                    "ledger-a",
                    "generation-1",
                    "generation-2",
                    "disposable-host-resource-42",
                    "disposable-storage-resource-42",
                    new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero),
                    counts,
                    resources,
                    ["administrator-access", "deployment-configuration", "protected-audit"],
                    string.Empty);
                var plan = draft with { PlanFingerprint = LedgerResetPlanBinding.ComputeFingerprint(draft) };
                var backup = new FakeBackupGate
                {
                    Evidence = new LedgerResetBackupEvidence(
                        recoverySetId,
                        IsSucceeded: true,
                        IsIntegrityVerified: true,
                        IsIndependentlyProtected: true,
                        plan.CreatedUtc.AddMinutes(1))
                };
                var journal = new FakeJournalStore();
                var generation = new FakeGenerationGuard(plan.CurrentGenerationId, operationId, executed);
                var adapters = LedgerResetCoordinator.OrderedStages
                    .Select(stage => new FakeStageAdapter(
                        stage,
                        executed,
                        new LedgerResetStageEvidence(
                            stage,
                            operationId,
                            plan.PlanFingerprint,
                            plan.LedgerId,
                            plan.CurrentGenerationId,
                            plan.NewGenerationId,
                            AffectsOnlySelectedLedger: true,
                            OldWorkIsolated: true,
                            RequiredResourcesReady: true,
                            RemainingBusinessEntityCounts: new Dictionary<string, long>())))
                    .ToArray();
                return new ResetScenario(plan, backup, journal, generation, adapters, executed);
            }

            internal FakeStageAdapter Adapter(LedgerResetStage stage) =>
                Adapters.Single(adapter => adapter.Stage == stage);
        }

        private sealed class FakeBackupGate : ILedgerResetPreBackupGate
        {
            internal LedgerResetBackupEvidence Evidence { get; set; } = null!;
            internal int ExecutionCount { get; private set; }

            public Task<LedgerResetBackupEvidence> RequireFreshBackupAsync(
                LedgerResetPlan plan,
                CancellationToken cancellationToken)
            {
                ExecutionCount++;
                return Task.FromResult(Evidence);
            }
        }

        private sealed class FakeStageAdapter(
            LedgerResetStage stage,
            List<LedgerResetStage> executed,
            LedgerResetStageEvidence evidence) : ILedgerResetStageAdapter
        {
            public LedgerResetStage Stage { get; } = stage;
            internal LedgerResetStageEvidence Evidence { get; set; } = evidence;
            internal Exception? Failure { get; set; }
            internal int ExecutionCount { get; private set; }

            public Task<LedgerResetStageEvidence> ExecuteAsync(
                LedgerResetContext context,
                CancellationToken cancellationToken)
            {
                ExecutionCount++;
                if (Failure is not null)
                {
                    throw Failure;
                }

                executed.Add(Stage);
                return Task.FromResult(Evidence);
            }
        }

        private sealed class FakeGenerationGuard(
            string activeGenerationId,
            Guid operationId,
            IReadOnlyCollection<LedgerResetStage> executed) : ILedgerResetGenerationGuard
        {
            internal string ActiveGenerationId { get; set; } = activeGenerationId;
            internal int ActivationAttempts { get; private set; }
            internal int ActivationObservedStageCount { get; private set; }
            internal bool ThrowAfterFirstActivation { get; set; }

            public Task<LedgerResetGenerationState> GetAsync(
                string environmentId,
                string ledgerId,
                CancellationToken cancellationToken)
            {
                Guid? activatedBy = ActiveGenerationId == activeGenerationId ? null : operationId;
                return Task.FromResult(new LedgerResetGenerationState(
                    ActiveGenerationId,
                    ActiveGenerationId == activeGenerationId ? null : activeGenerationId,
                    activatedBy,
                    PreviousGenerationAvailable: ActiveGenerationId != activeGenerationId));
            }

            public Task<LedgerResetActivationEvidence> TryActivateAsync(
                Guid requestedOperationId,
                string environmentId,
                string ledgerId,
                string expectedGenerationId,
                string newGenerationId,
                CancellationToken cancellationToken)
            {
                ActivationAttempts++;
                ActivationObservedStageCount = executed.Count;
                ActiveGenerationId.Should().Be(expectedGenerationId);
                ActiveGenerationId = newGenerationId;
                if (ThrowAfterFirstActivation)
                {
                    ThrowAfterFirstActivation = false;
                    throw new LedgerResetActivationOutcomeUnknownException();
                }

                return Task.FromResult(new LedgerResetActivationEvidence(
                    requestedOperationId,
                    expectedGenerationId,
                    newGenerationId,
                    WasCompareAndSwapApplied: true,
                    PreviousGenerationAvailable: true));
            }
        }

        private sealed class FakeJournalStore : ILedgerResetJournalStore
        {
            private LedgerResetJournal? _journal;

            public Task<LedgerResetJournal?> LoadAsync(Guid operationId, CancellationToken cancellationToken) =>
                Task.FromResult(_journal);

            public Task<LedgerResetJournal> CreateAsync(
                LedgerResetJournal journal,
                CancellationToken cancellationToken)
            {
                _journal = journal;
                return Task.FromResult(journal);
            }

            public Task<LedgerResetJournal> RecordPreBackupAsync(
                Guid operationId,
                long expectedRevision,
                LedgerResetBackupEvidence evidence,
                CancellationToken cancellationToken)
            {
                _journal = Require(expectedRevision) with
                {
                    PreBackup = evidence,
                    Revision = expectedRevision + 1,
                    State = LedgerResetState.Running,
                    ErrorCode = null
                };
                return Task.FromResult(_journal);
            }

            public Task<LedgerResetJournal> RecordStageAsync(
                Guid operationId,
                long expectedRevision,
                LedgerResetStage stage,
                LedgerResetStageEvidence evidence,
                CancellationToken cancellationToken)
            {
                var current = Require(expectedRevision);
                _journal = current with
                {
                    CompletedStages = current.CompletedStages.Append(stage).ToArray(),
                    Revision = expectedRevision + 1,
                    State = LedgerResetState.Running,
                    ErrorCode = null
                };
                return Task.FromResult(_journal);
            }

            public Task<LedgerResetJournal> RecordActivationAsync(
                Guid operationId,
                long expectedRevision,
                LedgerResetActivationEvidence evidence,
                CancellationToken cancellationToken)
            {
                _journal = Require(expectedRevision) with
                {
                    Activation = evidence,
                    Revision = expectedRevision + 1,
                    State = LedgerResetState.Succeeded,
                    ErrorCode = null
                };
                return Task.FromResult(_journal);
            }

            public Task MarkFailedAsync(
                Guid operationId,
                long expectedRevision,
                string errorCode,
                CancellationToken cancellationToken)
            {
                _journal = Require(expectedRevision) with
                {
                    Revision = expectedRevision + 1,
                    State = LedgerResetState.Failed,
                    ErrorCode = errorCode
                };
                return Task.CompletedTask;
            }

            private LedgerResetJournal Require(long expectedRevision)
            {
                _journal.Should().NotBeNull();
                _journal!.Revision.Should().Be(expectedRevision);
                return _journal;
            }
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
