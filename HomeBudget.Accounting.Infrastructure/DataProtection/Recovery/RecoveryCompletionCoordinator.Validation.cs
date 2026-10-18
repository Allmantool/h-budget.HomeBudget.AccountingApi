using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed partial class RecoveryCompletionCoordinator
    {
        private static readonly RecoveryExecutionStage[] RestoreStages =
        [
            RecoveryExecutionStage.PrepareTarget,
            RecoveryExecutionStage.RestoreSql,
            RecoveryExecutionStage.RestoreEventStore,
            RecoveryExecutionStage.RestoreMongo,
            RecoveryExecutionStage.RestoreRates,
            RecoveryExecutionStage.ReconstructMessaging,
            RecoveryExecutionStage.ReconstructProcessingState
        ];

        private static void ValidateRequest(RecoveryExecutionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Plan);
            ArgumentNullException.ThrowIfNull(request.Artifact);
            ArgumentNullException.ThrowIfNull(request.Target);
            if (request.Plan.ActivationAllowed || !request.Plan.SuppressNotifications ||
                !request.Plan.SuppressExternalSideEffects)
            {
                throw Invalid("recovery-plan-unsafe", "Recovery completion requires replay side-effect suppression.");
            }

            if (request.Artifact.RecoverySetId != request.Plan.BaselineRecoverySetId ||
                !string.Equals(request.Artifact.PlanFingerprint, request.Plan.PlanFingerprint, StringComparison.Ordinal) ||
                !request.Artifact.IsChecksumVerified)
            {
                throw Invalid("manifest-binding-mismatch", "Recovery completion is not bound to the verified plan.");
            }
        }

        private static void ValidateJournal(
            RecoveryExecutionRequest request,
            RecoveryExecutionJournal journal)
        {
            if (journal.OperationId != request.OperationId || journal.Artifact != request.Artifact ||
                journal.Target != request.Target ||
                !string.Equals(journal.ExpectedActiveGenerationId, request.Target.ActiveGenerationId, StringComparison.Ordinal))
            {
                throw Invalid("resume-binding-conflict", "Recovery completion does not match the durable restore journal.");
            }

            if (journal.CompletedStages is null || journal.Revision < journal.CompletedStages.Count ||
                !HasOrderedRestoreAndCompletionPrefix(journal.CompletedStages))
            {
                throw Invalid("restore-journal-invalid", "Recovery completion journal checkpoints are invalid.");
            }

            if (!RestoreStages.All(journal.CompletedStages.Contains))
            {
                throw Invalid("restore-incomplete", "All isolated restore stages must complete before replay.");
            }

            var completionCount = journal.CompletedStages.Count - RestoreStages.Length;
            if ((journal.State == RecoveryExecutionState.ReadyToActivate ||
                 journal.State == RecoveryExecutionState.Activated) &&
                completionCount != CompletionStages.Length)
            {
                throw Invalid("restore-journal-invalid", "Activation journal is missing validation stages.");
            }

            if (!journal.RollbackEligible)
            {
                throw Invalid("restore-journal-invalid", "Recovery completion is not rollback eligible.");
            }
        }

        private static bool HasOrderedRestoreAndCompletionPrefix(
            IReadOnlyCollection<RecoveryExecutionStage> completedStages)
        {
            var expected = RestoreStages.Concat(CompletionStages).ToArray();
            return completedStages.Count <= expected.Length &&
                completedStages.Select((stage, index) => stage == expected[index]).All(matches => matches);
        }

        private static void ValidateReplayEvidence(
            RecoveryCompletionContext context,
            RecoveryPendingReplayEvidence evidence)
        {
            if (evidence is null || !BindingMatches(context, evidence.Binding))
            {
                throw Invalid("completion-evidence-mismatch", "Pending replay evidence is not bound to this recovery.");
            }

            if (evidence.AcceptanceCutoffUtc != context.Plan.ActualCutoffUtc ||
                evidence.AcceptedCommandCount < 0 ||
                evidence.SettledCommandCount != evidence.AcceptedCommandCount ||
                evidence.DuplicateFinancialEffectCount != 0 ||
                !evidence.IdempotencyKeysPreserved)
            {
                throw Invalid("pending-replay-unsettled", "Accepted pending work is not durably settled exactly once.");
            }

            RequireSuppression(evidence.NotificationsSuppressed, evidence.ExternalSideEffectsSuppressed);
        }

        private static void ValidateRebuildEvidence(
            RecoveryCompletionContext context,
            RecoveryProjectionRebuildEvidence evidence)
        {
            if (evidence is null || !BindingMatches(context, evidence.Binding))
            {
                throw Invalid("completion-evidence-mismatch", "Projection rebuild evidence is not bound to this recovery.");
            }

            if (evidence.ProjectionCutoffUtc != context.Plan.ActualCutoffUtc ||
                !evidence.DerivedStateRebuilt || !evidence.CheckpointsRebuilt ||
                !evidence.AuthoritativeDataPreserved)
            {
                throw Invalid("projection-rebuild-incomplete", "Projection rebuild did not reproduce the selected cutoff.");
            }

            RequireSuppression(evidence.NotificationsSuppressed, evidence.ExternalSideEffectsSuppressed);
        }

        private static void ValidateReconciliationEvidence(
            RecoveryCompletionContext context,
            RecoveryApiReconciliationEvidence evidence)
        {
            if (evidence is null || !BindingMatches(context, evidence.Binding))
            {
                throw Invalid("completion-evidence-mismatch", "API reconciliation evidence is not bound to this recovery.");
            }

            if (evidence.ReconciledCutoffUtc != context.Plan.ActualCutoffUtc ||
                !evidence.Passed || !evidence.IdentitiesAndCountsMatch ||
                !evidence.NoDuplicatesOrOrphans || !evidence.ReferencesValid ||
                !evidence.PerAccountPeriodCurrencyTotalsMatch || !evidence.BalancesMatch ||
                !evidence.OpeningBalancesAndAdjustmentsMatch || !evidence.SplitsMatch ||
                !evidence.TransfersRatesAndFeesMatch || !evidence.SettingsAndRequiredRatesMatch)
            {
                throw Invalid("reconciliation-failed", "Recovered state did not reconcile through supported APIs.");
            }
        }

        private static void ValidateActivationEvidence(
            RecoveryExecutionRequest request,
            string visibleBefore,
            RecoveryActivationEvidence evidence)
        {
            if (evidence is null || evidence.OperationId != request.OperationId || !evidence.IsAtomic ||
                !string.Equals(evidence.EnvironmentId, request.Target.EnvironmentId, StringComparison.Ordinal) ||
                !string.Equals(evidence.LedgerId, request.Target.LedgerId, StringComparison.Ordinal) ||
                !string.Equals(evidence.PreviousGenerationId, request.Target.ActiveGenerationId, StringComparison.Ordinal) ||
                !string.Equals(evidence.VisibleGenerationId, request.Target.GenerationId, StringComparison.Ordinal) ||
                !string.Equals(evidence.PlanFingerprint, request.Plan.PlanFingerprint, StringComparison.Ordinal))
            {
                throw Invalid("activation-evidence-mismatch", "Generation activation evidence is not bound to this recovery.");
            }

            var validTransition = evidence.Outcome == RecoveryActivationOutcome.Activated &&
                string.Equals(visibleBefore, request.Target.ActiveGenerationId, StringComparison.Ordinal);
            var validResume = evidence.Outcome == RecoveryActivationOutcome.AlreadyActivatedByOperation &&
                string.Equals(visibleBefore, request.Target.GenerationId, StringComparison.Ordinal);
            if (!validTransition && !validResume)
            {
                throw Invalid("activation-conflict", "Generation compare-and-swap did not activate this recovery operation.");
            }
        }

        private static void RequireSuppression(bool notificationsSuppressed, bool externalSideEffectsSuppressed)
        {
            if (!notificationsSuppressed || !externalSideEffectsSuppressed)
            {
                throw Invalid("replay-side-effects-not-suppressed", "Recovery replay side effects are not suppressed.");
            }
        }

        private static bool BindingMatches(RecoveryCompletionContext context, RecoveryCompletionBinding binding) =>
            binding == ExpectedBinding(context);

        private static RecoveryCompletionBinding ExpectedBinding(RecoveryCompletionContext context) => new(
            context.OperationId,
            context.Artifact.RecoverySetId,
            context.Plan.PlanFingerprint,
            context.Artifact.ManifestSha256,
            context.Target.TargetId,
            context.Target.GenerationId);
    }
}
