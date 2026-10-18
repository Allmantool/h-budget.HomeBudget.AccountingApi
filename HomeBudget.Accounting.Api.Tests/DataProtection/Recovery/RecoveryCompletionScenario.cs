using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Recovery
{
    internal sealed class RecoveryCompletionScenario
    {
        private readonly HashSet<string> _financialEffects = new(StringComparer.Ordinal);

        public RecoveryCompletionScenario()
        {
            Guard = new FakeRecoveryActiveGenerationGuard();
            Replay = new ReplayAdapter(this);
            Rebuild = new RebuildAdapter(this);
            Reconciliation = new ReconciliationAdapter(this);
            Activator = new ActivationAdapter(this);
        }

        public IList<RecoveryExecutionStage> Calls { get; } = new List<RecoveryExecutionStage>();

        public FakeRecoveryActiveGenerationGuard Guard { get; }

        public ReplayAdapter Replay { get; }

        public RebuildAdapter Rebuild { get; }

        public ReconciliationAdapter Reconciliation { get; }

        public ActivationAdapter Activator { get; }

        public RecoveryExecutionStage? FailStageOnce { get; set; }

        public int FinancialEffectCount => _financialEffects.Count;

        public RecoveryCompletionCoordinator Coordinator(InMemoryRecoveryExecutionJournalStore store) => new(
            store,
            Guard,
            Replay,
            Rebuild,
            Reconciliation,
            Activator);

        public static async Task<InMemoryRecoveryExecutionJournalStore> RestoredStoreAsync()
        {
            var store = new InMemoryRecoveryExecutionJournalStore();
            var request = RecoveryExecutionScenario.Request();
            await new RecoveryExecutionCoordinator(
                    store,
                    new FakeRecoveryActiveGenerationGuard(),
                    RecoveryExecutionScenario.Adapters(new List<RecoveryExecutionStage>()))
                .ExecuteAsync(request, CancellationToken.None);
            return store;
        }

        private RecoveryCompletionBinding Binding(RecoveryCompletionContext context) => new(
            context.OperationId,
            context.Artifact.RecoverySetId,
            context.Plan.PlanFingerprint,
            context.Artifact.ManifestSha256,
            context.Target.TargetId,
            context.Target.GenerationId);

        private void Before(RecoveryExecutionStage stage)
        {
            Calls.Add(stage);
            if (FailStageOnce == stage)
            {
                FailStageOnce = null;
                throw new InvalidOperationException("synthetic completion stage failure");
            }
        }

        internal sealed class ReplayAdapter : IRecoveryPendingReplayAdapter
        {
            private readonly RecoveryCompletionScenario _owner;

            public ReplayAdapter(RecoveryCompletionScenario owner)
            {
                _owner = owner;
            }

            public int InvocationCount { get; private set; }

            public RecoveryCompletionContext LastContext { get; private set; }

            public bool SettlesAll { get; set; } = true;

            public bool PreservesIdempotency { get; set; } = true;

            public bool SuppressesSideEffects { get; set; } = true;

            public bool CancelOnce { get; set; }

            public Task<RecoveryPendingReplayEvidence> ReplayAsync(
                RecoveryCompletionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InvocationCount++;
                LastContext = context;
                _owner.Before(RecoveryExecutionStage.ReplayAcceptedPending);
                if (CancelOnce)
                {
                    CancelOnce = false;
                    throw new OperationCanceledException("synthetic cancellation");
                }

                _owner._financialEffects.Add("pending-1");
                _owner._financialEffects.Add("pending-2");
                _owner._financialEffects.Add("pending-3");
                return Task.FromResult(new RecoveryPendingReplayEvidence(
                    _owner.Binding(context),
                    context.Plan.ActualCutoffUtc,
                    3,
                    SettlesAll ? 3 : 2,
                    0,
                    PreservesIdempotency,
                    SuppressesSideEffects && context.SuppressNotifications,
                    SuppressesSideEffects && context.SuppressExternalSideEffects));
            }
        }

        internal sealed class RebuildAdapter : IRecoveryProjectionRebuildAdapter
        {
            private readonly RecoveryCompletionScenario _owner;

            public RebuildAdapter(RecoveryCompletionScenario owner)
            {
                _owner = owner;
            }

            public bool CompletesRebuild { get; set; } = true;

            public Task<RecoveryProjectionRebuildEvidence> RebuildAsync(
                RecoveryCompletionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _owner.Before(RecoveryExecutionStage.RebuildProjections);
                return Task.FromResult(new RecoveryProjectionRebuildEvidence(
                    _owner.Binding(context),
                    context.Plan.ActualCutoffUtc,
                    CompletesRebuild,
                    CompletesRebuild,
                    CompletesRebuild,
                    context.SuppressNotifications,
                    context.SuppressExternalSideEffects));
            }
        }

        internal sealed class ReconciliationAdapter : IRecoveryApiReconciliationAdapter
        {
            private readonly RecoveryCompletionScenario _owner;

            public ReconciliationAdapter(RecoveryCompletionScenario owner)
            {
                _owner = owner;
            }

            public bool Passes { get; set; } = true;

            public bool ReturnWrongBinding { get; set; }

            public Task<RecoveryApiReconciliationEvidence> ReconcileAsync(
                RecoveryCompletionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _owner.Before(RecoveryExecutionStage.ReconcileApis);
                var binding = _owner.Binding(context);
                if (ReturnWrongBinding)
                {
                    binding = binding with { TargetGenerationId = "generation-other" };
                }

                return Task.FromResult(new RecoveryApiReconciliationEvidence(
                    binding,
                    context.Plan.ActualCutoffUtc,
                    Passes,
                    Passes,
                    Passes,
                    Passes,
                    Passes,
                    Passes,
                    Passes,
                    Passes,
                    Passes,
                    Passes));
            }
        }

        internal sealed class ActivationAdapter : IRecoveryGenerationActivator
        {
            private readonly RecoveryCompletionScenario _owner;
            private Guid? _activatedBy;

            public ActivationAdapter(RecoveryCompletionScenario owner)
            {
                _owner = owner;
            }

            public int InvocationCount { get; private set; }

            public bool FailBeforeSwapOnce { get; set; }

            public Task<RecoveryActivationEvidence> CompareAndSwapAsync(
                RecoveryActivationRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InvocationCount++;
                if (FailBeforeSwapOnce)
                {
                    FailBeforeSwapOnce = false;
                    throw new InvalidOperationException("synthetic compare-and-swap failure");
                }

                RecoveryActivationOutcome outcome;
                if (_owner.Guard.ActiveGenerationId == request.ExpectedActiveGenerationId)
                {
                    _owner.Guard.ActiveGenerationId = request.TargetGenerationId;
                    _activatedBy = request.OperationId;
                    outcome = RecoveryActivationOutcome.Activated;
                }
                else if (_owner.Guard.ActiveGenerationId == request.TargetGenerationId &&
                         _activatedBy == request.OperationId)
                {
                    outcome = RecoveryActivationOutcome.AlreadyActivatedByOperation;
                }
                else
                {
                    outcome = RecoveryActivationOutcome.Conflict;
                }

                return Task.FromResult(new RecoveryActivationEvidence(
                    request.OperationId,
                    request.EnvironmentId,
                    request.LedgerId,
                    request.ExpectedActiveGenerationId,
                    _owner.Guard.ActiveGenerationId,
                    request.PlanFingerprint,
                    outcome,
                    true));
            }
        }
    }
}
