using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed partial class RecoveryExecutionCoordinator
    {
        private static readonly RecoveryExecutionStage[] OrderedStages =
        [
            RecoveryExecutionStage.PrepareTarget,
            RecoveryExecutionStage.RestoreSql,
            RecoveryExecutionStage.RestoreEventStore,
            RecoveryExecutionStage.RestoreMongo,
            RecoveryExecutionStage.RestoreRates,
            RecoveryExecutionStage.ReconstructMessaging,
            RecoveryExecutionStage.ReconstructProcessingState
        ];

        private readonly IRecoveryExecutionJournalStore _journalStore;
        private readonly IRecoveryActiveGenerationGuard _activeGenerationGuard;
        private readonly IReadOnlyDictionary<RecoveryExecutionStage, IRecoveryRestoreStageAdapter> _adapters;

        public RecoveryExecutionCoordinator(
            IRecoveryExecutionJournalStore journalStore,
            IRecoveryActiveGenerationGuard activeGenerationGuard,
            IEnumerable<IRecoveryRestoreStageAdapter> adapters)
        {
            ArgumentNullException.ThrowIfNull(journalStore);
            ArgumentNullException.ThrowIfNull(activeGenerationGuard);
            ArgumentNullException.ThrowIfNull(adapters);
            _journalStore = journalStore;
            _activeGenerationGuard = activeGenerationGuard;
            _adapters = IndexAdapters(adapters);
        }

        public async Task<RecoveryExecutionResult> ExecuteAsync(
            RecoveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request);
            RequireAllAdapters();
            await RequireActiveGenerationUnchangedAsync(request, cancellationToken).ConfigureAwait(false);

            var journal = await LoadOrCreateJournalAsync(request, cancellationToken).ConfigureAwait(false);
            if (journal.State == RecoveryExecutionState.AwaitingReplayRebuild ||
                journal.State == RecoveryExecutionState.ReadyToActivate ||
                journal.State == RecoveryExecutionState.Activated ||
                journal.CompletedStages.Any(stage => stage >= RecoveryExecutionStage.ReplayAcceptedPending))
            {
                return Result(journal);
            }

            var context = new RecoveryRestoreContext(
                request.OperationId,
                request.Plan,
                request.Artifact,
                request.Target);
            try
            {
                foreach (var stage in OrderedStages)
                {
                    if (journal.CompletedStages.Contains(stage))
                    {
                        continue;
                    }

                    await RequireActiveGenerationUnchangedAsync(request, cancellationToken).ConfigureAwait(false);
                    var evidence = await _adapters[stage].ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                    ValidateCompletionEvidence(request, stage, evidence);
                    await RequireActiveGenerationUnchangedAsync(request, cancellationToken).ConfigureAwait(false);
                    journal = await _journalStore.RecordStageAsync(
                        request.OperationId,
                        journal.Revision,
                        stage,
                        evidence,
                        cancellationToken).ConfigureAwait(false);
                }

                await RequireActiveGenerationUnchangedAsync(request, cancellationToken).ConfigureAwait(false);
                journal = await _journalStore.MarkAwaitingReplayRebuildAsync(
                    request.OperationId,
                    journal.Revision,
                    cancellationToken).ConfigureAwait(false);
                return Result(journal);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await MarkFailedAsync(journal, exception).ConfigureAwait(false);
                if (exception is RecoveryExecutionException)
                {
                    throw;
                }

                throw Invalid("restore-stage-failed", "An isolated restore stage failed.");
            }
            catch (OperationCanceledException)
            {
                await MarkFailedAsync(journal, Invalid("restore-cancelled", "Isolated restore was cancelled."))
                    .ConfigureAwait(false);
                throw;
            }
        }

        private async Task<RecoveryExecutionJournal> LoadOrCreateJournalAsync(
            RecoveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var journal = await _journalStore.LoadAsync(request.OperationId, cancellationToken).ConfigureAwait(false);
            if (journal is not null)
            {
                ValidateResumeBinding(request, journal);
                return journal;
            }

            var initial = new RecoveryExecutionJournal(
                request.OperationId,
                request.Artifact,
                request.Target,
                request.Target.ActiveGenerationId,
                RecoveryExecutionState.Running,
                0,
                Array.Empty<RecoveryExecutionStage>(),
                true,
                null);
            journal = await _journalStore.CreateAsync(initial, cancellationToken).ConfigureAwait(false);
            ValidateResumeBinding(request, journal);
            return journal;
        }

        private async Task RequireActiveGenerationUnchangedAsync(
            RecoveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var current = await _activeGenerationGuard.GetActiveGenerationAsync(
                request.Target.EnvironmentId,
                request.Target.LedgerId,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current, request.Target.ActiveGenerationId, StringComparison.Ordinal))
            {
                throw Invalid("active-generation-changed", "The active ledger generation changed during isolated restore.");
            }
        }

        private async Task MarkFailedAsync(RecoveryExecutionJournal journal, Exception exception)
        {
            if (journal is null || journal.State == RecoveryExecutionState.AwaitingReplayRebuild)
            {
                return;
            }

            var code = exception is RecoveryExecutionException recoveryException
                ? recoveryException.Code
                : "restore-stage-failed";
            await _journalStore.MarkFailedAsync(
                journal.OperationId,
                journal.Revision,
                code,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static RecoveryExecutionResult Result(RecoveryExecutionJournal journal) => new(
            journal.OperationId,
            journal.State,
            journal.Revision,
            Array.AsReadOnly(journal.CompletedStages.ToArray()),
            journal.RollbackEligible,
            journal.State == RecoveryExecutionState.Activated);

        private static IReadOnlyDictionary<RecoveryExecutionStage, IRecoveryRestoreStageAdapter> IndexAdapters(
            IEnumerable<IRecoveryRestoreStageAdapter> adapters)
        {
            var materialized = adapters.ToArray();
            if (materialized.Any(item => item is null) ||
                materialized.GroupBy(item => item.Stage).Any(group => group.Count() != 1))
            {
                throw Invalid("restore-adapter-conflict", "Restore stage adapters are duplicated or invalid.");
            }

            return materialized.ToDictionary(item => item.Stage);
        }

        private void RequireAllAdapters()
        {
            if (_adapters.Count != OrderedStages.Length || OrderedStages.Any(stage => !_adapters.ContainsKey(stage)))
            {
                throw Invalid("restore-adapter-missing", "A required isolated restore stage adapter is unavailable.");
            }
        }

        private static RecoveryExecutionException Invalid(string code, string message) => new(code, message);
    }
}
