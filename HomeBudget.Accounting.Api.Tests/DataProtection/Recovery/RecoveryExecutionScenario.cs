using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    internal static class RecoveryExecutionScenario
    {
        public static readonly RecoveryExecutionStage[] OrderedStages =
        [
            RecoveryExecutionStage.PrepareTarget,
            RecoveryExecutionStage.RestoreSql,
            RecoveryExecutionStage.RestoreEventStore,
            RecoveryExecutionStage.RestoreMongo,
            RecoveryExecutionStage.RestoreRates,
            RecoveryExecutionStage.ReconstructMessaging,
            RecoveryExecutionStage.ReconstructProcessingState
        ];

        public static RecoveryExecutionRequest Request()
        {
            var plan = new RecoveryPlanSelector().Select(
                RecoveryPlanningScenario.Request(),
                RecoveryPlanningScenario.Inventory());
            var target = new RecoveryExecutionTargetEvidence(
                plan.Target.TargetId,
                plan.Target.EnvironmentId,
                plan.Target.LedgerId,
                plan.Target.GenerationId,
                plan.Target.ActiveGenerationId,
                true,
                true,
                true,
                "eventstore-restore-1",
                true,
                Resource("topic-generation-8", plan, true),
                Resource("group-generation-8", plan, true),
                Resource("subscription-generation-8", plan, true));
            return new RecoveryExecutionRequest(
                Guid.Parse("581d5fb0-d357-48b8-8308-a9210eb6f726"),
                plan,
                new RecoveryArtifactBinding(
                    plan.BaselineRecoverySetId,
                    plan.PlanFingerprint,
                    "home-ledger-recovery-set/v1",
                    new string('a', 64),
                    plan.ApplicationVersion,
                    plan.SchemaVersion,
                    true),
                target);
        }

        public static IReadOnlyCollection<IRecoveryRestoreStageAdapter> Adapters(
            IList<RecoveryExecutionStage> calls,
            RecoveryExecutionStage? failOnceAt = null,
            Action<RecoveryExecutionStage> afterStage = null) =>
            OrderedStages.Select(stage =>
                    (IRecoveryRestoreStageAdapter)new FakeRestoreStageAdapter(
                        stage,
                        calls,
                        failOnceAt == stage,
                        afterStage))
                .ToArray();

        private static RecoveryMessagingResourceIdentity Resource(
            string id,
            RecoveryPlan plan,
            bool dedicated) => new(
                id,
                plan.Target.LedgerId,
                plan.Target.GenerationId,
                dedicated);

        private sealed class FakeRestoreStageAdapter : IRecoveryRestoreStageAdapter
        {
            private readonly IList<RecoveryExecutionStage> _calls;
            private readonly Action<RecoveryExecutionStage> _afterStage;
            private bool _failOnce;

            public FakeRestoreStageAdapter(
                RecoveryExecutionStage stage,
                IList<RecoveryExecutionStage> calls,
                bool failOnce,
                Action<RecoveryExecutionStage> afterStage)
            {
                Stage = stage;
                _calls = calls;
                _failOnce = failOnce;
                _afterStage = afterStage;
            }

            public RecoveryExecutionStage Stage { get; }

            public Task<RecoveryStageCompletionEvidence> ExecuteAsync(
                RecoveryRestoreContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _calls.Add(Stage);
                if (_failOnce)
                {
                    _failOnce = false;
                    throw new InvalidOperationException("synthetic stage failure");
                }

                _afterStage?.Invoke(Stage);
                return Task.FromResult(new RecoveryStageCompletionEvidence(
                    Stage,
                    context.Artifact.RecoverySetId,
                    context.Plan.PlanFingerprint,
                    context.Artifact.ManifestSha256,
                    context.Target.TargetId,
                    context.Target.GenerationId,
                    true,
                    true,
                    true));
            }
        }
    }

    internal sealed class FakeRecoveryActiveGenerationGuard : IRecoveryActiveGenerationGuard
    {
        public string ActiveGenerationId { get; set; } = "generation-7";

        public Task<string> GetActiveGenerationAsync(
            string environmentId,
            string ledgerId,
            CancellationToken cancellationToken) => Task.FromResult(ActiveGenerationId);
    }

    internal sealed class InMemoryRecoveryExecutionJournalStore : IRecoveryCompletionJournalStore
    {
        private RecoveryExecutionJournal _journal;

        public RecoveryExecutionStage? FailCompletionCheckpointOnce { get; set; }

        public bool FailReadyCheckpointOnce { get; set; }

        public bool FailActivatedCheckpointOnce { get; set; }

        public RecoveryExecutionJournal Journal => _journal;

        public Task<RecoveryExecutionJournal> LoadAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(_journal?.OperationId == operationId ? _journal : null);

        public Task<RecoveryExecutionJournal> CreateAsync(
            RecoveryExecutionJournal journal,
            CancellationToken cancellationToken)
        {
            _journal ??= journal;
            return Task.FromResult(_journal);
        }

        public Task<RecoveryExecutionJournal> RecordStageAsync(
            Guid operationId,
            long expectedRevision,
            RecoveryExecutionStage stage,
            RecoveryStageCompletionEvidence evidence,
            CancellationToken cancellationToken)
        {
            RequireRevision(operationId, expectedRevision);
            _journal = _journal with
            {
                Revision = _journal.Revision + 1,
                State = RecoveryExecutionState.Running,
                CompletedStages = [.. _journal.CompletedStages, stage],
                FailureCode = null
            };
            return Task.FromResult(_journal);
        }

        public Task<RecoveryExecutionJournal> MarkFailedAsync(
            Guid operationId,
            long expectedRevision,
            string failureCode,
            bool rollbackEligible,
            CancellationToken cancellationToken)
        {
            RequireRevision(operationId, expectedRevision);
            _journal = _journal with
            {
                Revision = _journal.Revision + 1,
                State = RecoveryExecutionState.Failed,
                FailureCode = failureCode,
                RollbackEligible = rollbackEligible
            };
            return Task.FromResult(_journal);
        }

        public Task<RecoveryExecutionJournal> MarkAwaitingReplayRebuildAsync(
            Guid operationId,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            RequireRevision(operationId, expectedRevision);
            _journal = _journal with
            {
                Revision = _journal.Revision + 1,
                State = RecoveryExecutionState.AwaitingReplayRebuild,
                FailureCode = null,
                RollbackEligible = true
            };
            return Task.FromResult(_journal);
        }

        public Task<RecoveryExecutionJournal> RecordCompletionStageAsync(
            Guid operationId,
            long expectedRevision,
            RecoveryCompletionStageEvidence evidence,
            CancellationToken cancellationToken)
        {
            RequireRevision(operationId, expectedRevision);
            if (FailCompletionCheckpointOnce == evidence.Stage)
            {
                FailCompletionCheckpointOnce = null;
                throw new InvalidOperationException("synthetic completion checkpoint failure");
            }

            _journal = _journal with
            {
                Revision = _journal.Revision + 1,
                State = RecoveryExecutionState.Running,
                CompletedStages = [.. _journal.CompletedStages, evidence.Stage],
                FailureCode = null
            };
            return Task.FromResult(_journal);
        }

        public Task<RecoveryExecutionJournal> MarkReadyToActivateAsync(
            Guid operationId,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            RequireRevision(operationId, expectedRevision);
            if (FailReadyCheckpointOnce)
            {
                FailReadyCheckpointOnce = false;
                throw new InvalidOperationException("synthetic ready checkpoint failure");
            }

            _journal = _journal with
            {
                Revision = _journal.Revision + 1,
                State = RecoveryExecutionState.ReadyToActivate,
                FailureCode = null,
                RollbackEligible = true
            };
            return Task.FromResult(_journal);
        }

        public Task<RecoveryExecutionJournal> MarkActivatedAsync(
            Guid operationId,
            long expectedRevision,
            RecoveryActivationEvidence evidence,
            CancellationToken cancellationToken)
        {
            RequireRevision(operationId, expectedRevision);
            if (FailActivatedCheckpointOnce)
            {
                FailActivatedCheckpointOnce = false;
                throw new InvalidOperationException("synthetic activated checkpoint failure");
            }

            _journal = _journal with
            {
                Revision = _journal.Revision + 1,
                State = RecoveryExecutionState.Activated,
                FailureCode = null,
                RollbackEligible = true
            };
            return Task.FromResult(_journal);
        }

        private void RequireRevision(Guid operationId, long expectedRevision)
        {
            if (_journal is null || _journal.OperationId != operationId || _journal.Revision != expectedRevision)
            {
                throw new InvalidOperationException("journal concurrency conflict");
            }
        }
    }
}
