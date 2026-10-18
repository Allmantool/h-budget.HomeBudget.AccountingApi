using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed partial class RecoveryCompletionCoordinator
    {
        private static readonly RecoveryExecutionStage[] CompletionStages =
        [
            RecoveryExecutionStage.ReplayAcceptedPending,
            RecoveryExecutionStage.RebuildProjections,
            RecoveryExecutionStage.ReconcileApis
        ];

        private readonly IRecoveryCompletionJournalStore _journalStore;
        private readonly IRecoveryActiveGenerationGuard _activeGenerationGuard;
        private readonly IRecoveryPendingReplayAdapter _pendingReplayAdapter;
        private readonly IRecoveryProjectionRebuildAdapter _projectionRebuildAdapter;
        private readonly IRecoveryApiReconciliationAdapter _apiReconciliationAdapter;
        private readonly IRecoveryGenerationActivator _generationActivator;

        public RecoveryCompletionCoordinator(
            IRecoveryCompletionJournalStore journalStore,
            IRecoveryActiveGenerationGuard activeGenerationGuard,
            IRecoveryPendingReplayAdapter pendingReplayAdapter,
            IRecoveryProjectionRebuildAdapter projectionRebuildAdapter,
            IRecoveryApiReconciliationAdapter apiReconciliationAdapter,
            IRecoveryGenerationActivator generationActivator)
        {
            ArgumentNullException.ThrowIfNull(journalStore);
            ArgumentNullException.ThrowIfNull(activeGenerationGuard);
            ArgumentNullException.ThrowIfNull(pendingReplayAdapter);
            ArgumentNullException.ThrowIfNull(projectionRebuildAdapter);
            ArgumentNullException.ThrowIfNull(apiReconciliationAdapter);
            ArgumentNullException.ThrowIfNull(generationActivator);
            _journalStore = journalStore;
            _activeGenerationGuard = activeGenerationGuard;
            _pendingReplayAdapter = pendingReplayAdapter;
            _projectionRebuildAdapter = projectionRebuildAdapter;
            _apiReconciliationAdapter = apiReconciliationAdapter;
            _generationActivator = generationActivator;
        }

        public async Task<RecoveryCompletionResult> CompleteAsync(
            RecoveryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request);

            var journal = await _journalStore.LoadAsync(request.OperationId, cancellationToken).ConfigureAwait(false)
                ?? throw Invalid("restore-journal-missing", "The durable isolated restore journal is unavailable.");
            ValidateJournal(request, journal);

            if (journal.State == RecoveryExecutionState.Activated)
            {
                await RequireVisibleGenerationAsync(request, request.Target.GenerationId, cancellationToken)
                    .ConfigureAwait(false);
                return Result(journal, request.Target.GenerationId);
            }

            var activationCommitted = false;
            var activationStarted = false;
            try
            {
                if (journal.State != RecoveryExecutionState.ReadyToActivate)
                {
                    await RequireVisibleGenerationAsync(
                        request,
                        request.Target.ActiveGenerationId,
                        cancellationToken).ConfigureAwait(false);
                    journal = await CompleteStagesAsync(request, journal, cancellationToken).ConfigureAwait(false);
                    journal = await _journalStore.MarkReadyToActivateAsync(
                        request.OperationId,
                        journal.Revision,
                        cancellationToken).ConfigureAwait(false);
                }

                var currentGeneration = await GetVisibleGenerationAsync(request, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(currentGeneration, request.Target.ActiveGenerationId, StringComparison.Ordinal) &&
                    !string.Equals(currentGeneration, request.Target.GenerationId, StringComparison.Ordinal))
                {
                    throw Invalid("active-generation-changed", "The active generation changed before activation.");
                }

                activationStarted = true;
                var activation = await _generationActivator.CompareAndSwapAsync(
                    new RecoveryActivationRequest(
                        request.OperationId,
                        request.Target.EnvironmentId,
                        request.Target.LedgerId,
                        request.Target.ActiveGenerationId,
                        request.Target.GenerationId,
                        request.Plan.PlanFingerprint),
                    cancellationToken).ConfigureAwait(false);
                ValidateActivationEvidence(request, currentGeneration, activation);
                await RequireVisibleGenerationAsync(request, request.Target.GenerationId, cancellationToken)
                    .ConfigureAwait(false);
                activationCommitted = true;
                try
                {
                    journal = await _journalStore.MarkActivatedAsync(
                        request.OperationId,
                        journal.Revision,
                        activation,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw Invalid("activation-checkpoint-failed", "Activation succeeded but its journal checkpoint failed.");
                }

                return Result(journal, request.Target.GenerationId);
            }
            catch (OperationCanceledException)
            {
                if (!activationCommitted)
                {
                    await MarkFailedAsync(journal, "recovery-completion-cancelled").ConfigureAwait(false);
                }

                throw;
            }
            catch (RecoveryExecutionException exception)
            {
                if (!activationCommitted)
                {
                    await MarkFailedAsync(journal, exception.Code).ConfigureAwait(false);
                }

                throw;
            }
            catch (Exception)
            {
                var code = activationStarted ? "activation-failed" : "recovery-completion-stage-failed";
                if (!activationCommitted)
                {
                    await MarkFailedAsync(journal, code).ConfigureAwait(false);
                }

                throw Invalid(code, activationStarted
                    ? "Generation compare-and-swap activation failed."
                    : "Recovery replay, rebuild, or reconciliation failed.");
            }
        }

        private async Task<RecoveryExecutionJournal> CompleteStagesAsync(
            RecoveryExecutionRequest request,
            RecoveryExecutionJournal journal,
            CancellationToken cancellationToken)
        {
            var context = new RecoveryCompletionContext(
                request.OperationId,
                request.Plan,
                request.Artifact,
                request.Target,
                true,
                true);

            foreach (var stage in CompletionStages)
            {
                if (journal.CompletedStages.Contains(stage))
                {
                    continue;
                }

                await RequireVisibleGenerationAsync(
                    request,
                    request.Target.ActiveGenerationId,
                    cancellationToken).ConfigureAwait(false);
                var completion = await ExecuteStageAsync(stage, context, cancellationToken).ConfigureAwait(false);
                await RequireVisibleGenerationAsync(
                    request,
                    request.Target.ActiveGenerationId,
                    cancellationToken).ConfigureAwait(false);
                journal = await _journalStore.RecordCompletionStageAsync(
                    request.OperationId,
                    journal.Revision,
                    completion,
                    cancellationToken).ConfigureAwait(false);
            }

            return journal;
        }

        private async Task<RecoveryCompletionStageEvidence> ExecuteStageAsync(
            RecoveryExecutionStage stage,
            RecoveryCompletionContext context,
            CancellationToken cancellationToken)
        {
            string canonicalEvidence;
            switch (stage)
            {
                case RecoveryExecutionStage.ReplayAcceptedPending:
                    var replay = await _pendingReplayAdapter.ReplayAsync(context, cancellationToken).ConfigureAwait(false);
                    ValidateReplayEvidence(context, replay);
                    canonicalEvidence = string.Join(
                        "\n",
                        replay.AcceptanceCutoffUtc.ToString("O"),
                        replay.AcceptedCommandCount,
                        replay.SettledCommandCount,
                        replay.DuplicateFinancialEffectCount,
                        replay.IdempotencyKeysPreserved,
                        replay.NotificationsSuppressed,
                        replay.ExternalSideEffectsSuppressed);
                    break;
                case RecoveryExecutionStage.RebuildProjections:
                    var rebuild = await _projectionRebuildAdapter.RebuildAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                    ValidateRebuildEvidence(context, rebuild);
                    canonicalEvidence = string.Join(
                        "\n",
                        rebuild.ProjectionCutoffUtc.ToString("O"),
                        rebuild.DerivedStateRebuilt,
                        rebuild.CheckpointsRebuilt,
                        rebuild.AuthoritativeDataPreserved,
                        rebuild.NotificationsSuppressed,
                        rebuild.ExternalSideEffectsSuppressed);
                    break;
                case RecoveryExecutionStage.ReconcileApis:
                    var reconciliation = await _apiReconciliationAdapter.ReconcileAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                    ValidateReconciliationEvidence(context, reconciliation);
                    canonicalEvidence = string.Join(
                        "\n",
                        reconciliation.ReconciledCutoffUtc.ToString("O"),
                        reconciliation.Passed,
                        reconciliation.IdentitiesAndCountsMatch,
                        reconciliation.NoDuplicatesOrOrphans,
                        reconciliation.ReferencesValid,
                        reconciliation.PerAccountPeriodCurrencyTotalsMatch,
                        reconciliation.BalancesMatch,
                        reconciliation.OpeningBalancesAndAdjustmentsMatch,
                        reconciliation.SplitsMatch,
                        reconciliation.TransfersRatesAndFeesMatch,
                        reconciliation.SettingsAndRequiredRatesMatch);
                    break;
                default:
                    throw Invalid("completion-stage-invalid", "An unsupported recovery completion stage was requested.");
            }

            return new RecoveryCompletionStageEvidence(
                stage,
                ExpectedBinding(context),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalEvidence))));
        }

        private async Task<string> GetVisibleGenerationAsync(
            RecoveryExecutionRequest request,
            CancellationToken cancellationToken) =>
            await _activeGenerationGuard.GetActiveGenerationAsync(
                request.Target.EnvironmentId,
                request.Target.LedgerId,
                cancellationToken).ConfigureAwait(false);

        private async Task RequireVisibleGenerationAsync(
            RecoveryExecutionRequest request,
            string expectedGenerationId,
            CancellationToken cancellationToken)
        {
            var current = await GetVisibleGenerationAsync(request, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current, expectedGenerationId, StringComparison.Ordinal))
            {
                throw Invalid("active-generation-changed", "The visible ledger generation changed during recovery.");
            }
        }

        private async Task MarkFailedAsync(RecoveryExecutionJournal journal, string code)
        {
            var current = await _journalStore.LoadAsync(journal.OperationId, CancellationToken.None).ConfigureAwait(false);
            if (current is null || current.State == RecoveryExecutionState.Activated)
            {
                return;
            }

            await _journalStore.MarkFailedAsync(
                current.OperationId,
                current.Revision,
                code,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static RecoveryCompletionResult Result(RecoveryExecutionJournal journal, string visibleGenerationId) =>
            new(
                journal.OperationId,
                journal.State,
                journal.Revision,
                visibleGenerationId,
                journal.State == RecoveryExecutionState.Activated,
                journal.RollbackEligible);

        private static RecoveryExecutionException Invalid(string code, string message) => new(code, message);
    }
}
