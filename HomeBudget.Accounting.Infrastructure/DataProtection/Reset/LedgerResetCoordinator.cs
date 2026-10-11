#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Reset
{
    public sealed class LedgerResetCoordinator
    {
        public static IReadOnlyList<LedgerResetStage> OrderedStages { get; } = Array.AsReadOnly(
        [
            LedgerResetStage.DisposePendingWork,
            LedgerResetStage.IsolateMessaging,
            LedgerResetStage.IsolateEventStore,
            LedgerResetStage.IsolateMongo,
            LedgerResetStage.IsolateSql,
            LedgerResetStage.ClearCachesAndJobs,
            LedgerResetStage.PrepareGeneration,
            LedgerResetStage.VerifyPristine
        ]);

        private readonly ILedgerResetJournalStore _journalStore;
        private readonly ILedgerResetPreBackupGate _preBackupGate;
        private readonly ILedgerResetGenerationGuard _generationGuard;
        private readonly IReadOnlyDictionary<LedgerResetStage, ILedgerResetStageAdapter> _adapters;

        public LedgerResetCoordinator(
            ILedgerResetJournalStore journalStore,
            ILedgerResetPreBackupGate preBackupGate,
            ILedgerResetGenerationGuard generationGuard,
            IEnumerable<ILedgerResetStageAdapter> adapters)
        {
            ArgumentNullException.ThrowIfNull(journalStore);
            ArgumentNullException.ThrowIfNull(preBackupGate);
            ArgumentNullException.ThrowIfNull(generationGuard);
            ArgumentNullException.ThrowIfNull(adapters);
            _journalStore = journalStore;
            _preBackupGate = preBackupGate;
            _generationGuard = generationGuard;
            _adapters = IndexAdapters(adapters);
        }

        public async Task<LedgerResetResult> ExecuteAsync(
            LedgerResetRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePlan(request.Plan);
            RequireAllAdapters();

            var journal = await LoadOrCreateAsync(request.Plan, cancellationToken).ConfigureAwait(false);
            var generation = await RequireExpectedGenerationAsync(request.Plan, journal, cancellationToken)
                .ConfigureAwait(false);
            if (generation.ActiveGenerationId == request.Plan.NewGenerationId)
            {
                return await CompleteObservedActivationAsync(request.Plan, journal, generation, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                if (journal.PreBackup is null)
                {
                    var backup = await _preBackupGate.RequireFreshBackupAsync(request.Plan, cancellationToken)
                        .ConfigureAwait(false);
                    ValidateBackup(request.Plan, backup);
                    journal = await _journalStore.RecordPreBackupAsync(
                        request.Plan.OperationId,
                        journal.Revision,
                        backup,
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var stage in OrderedStages)
                {
                    if (journal.CompletedStages.Contains(stage))
                    {
                        continue;
                    }

                    await RequireOldGenerationAsync(request.Plan, cancellationToken).ConfigureAwait(false);
                    var evidence = await _adapters[stage].ExecuteAsync(
                        new LedgerResetContext(request.Plan),
                        cancellationToken).ConfigureAwait(false);
                    ValidateStageEvidence(request.Plan, stage, evidence);
                    await RequireOldGenerationAsync(request.Plan, cancellationToken).ConfigureAwait(false);
                    journal = await _journalStore.RecordStageAsync(
                        request.Plan.OperationId,
                        journal.Revision,
                        stage,
                        evidence,
                        cancellationToken).ConfigureAwait(false);
                }

                var activation = await _generationGuard.TryActivateAsync(
                    request.Plan.OperationId,
                    request.Plan.EnvironmentId,
                    request.Plan.LedgerId,
                    request.Plan.CurrentGenerationId,
                    request.Plan.NewGenerationId,
                    cancellationToken).ConfigureAwait(false);
                ValidateActivation(request.Plan, activation);
                journal = await _journalStore.RecordActivationAsync(
                    request.Plan.OperationId,
                    journal.Revision,
                    activation,
                    cancellationToken).ConfigureAwait(false);
                return ToResult(journal);
            }
            catch (LedgerResetActivationOutcomeUnknownException)
            {
                await MarkFailedAsync(journal, "activation-outcome-unknown").ConfigureAwait(false);
                throw Invalid(
                    "activation-outcome-unknown",
                    "Generation activation outcome is unknown; resume must reconcile the durable activation state.");
            }
            catch (OperationCanceledException)
            {
                await MarkFailedAsync(journal, "reset-cancelled").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                var code = exception is LedgerResetException resetException
                    ? resetException.Code
                    : "reset-stage-failed";
                await MarkFailedAsync(journal, code).ConfigureAwait(false);
                if (exception is LedgerResetException)
                {
                    throw;
                }

                throw Invalid("reset-stage-failed", "A generation-isolated reset stage failed.");
            }
        }

        private async Task<LedgerResetJournal> LoadOrCreateAsync(
            LedgerResetPlan plan,
            CancellationToken cancellationToken)
        {
            var journal = await _journalStore.LoadAsync(plan.OperationId, cancellationToken).ConfigureAwait(false);
            if (journal is null)
            {
                journal = await _journalStore.CreateAsync(
                    new LedgerResetJournal(
                        plan.OperationId,
                        plan.PlanFingerprint,
                        plan.EnvironmentId,
                        plan.LedgerId,
                        plan.CurrentGenerationId,
                        plan.NewGenerationId,
                        LedgerResetState.Running,
                        0,
                        Array.Empty<LedgerResetStage>(),
                        null,
                        null,
                        RollbackEligible: true,
                        ErrorCode: null),
                    cancellationToken).ConfigureAwait(false);
            }

            ValidateJournal(plan, journal);
            return journal;
        }

        private async Task<LedgerResetGenerationState> RequireExpectedGenerationAsync(
            LedgerResetPlan plan,
            LedgerResetJournal journal,
            CancellationToken cancellationToken)
        {
            var state = await _generationGuard.GetAsync(
                plan.EnvironmentId,
                plan.LedgerId,
                cancellationToken).ConfigureAwait(false);
            var isOld = state.ActiveGenerationId == plan.CurrentGenerationId;
            var isOwnNew = state.ActiveGenerationId == plan.NewGenerationId &&
                state.ActivatedByOperationId == plan.OperationId &&
                journal.PreBackup is not null &&
                OrderedStages.SequenceEqual(journal.CompletedStages);
            if (!isOld && !isOwnNew)
            {
                throw Invalid("active-generation-changed", "The active ledger generation does not match the reset plan.");
            }

            return state;
        }

        private async Task RequireOldGenerationAsync(
            LedgerResetPlan plan,
            CancellationToken cancellationToken)
        {
            var state = await _generationGuard.GetAsync(
                plan.EnvironmentId,
                plan.LedgerId,
                cancellationToken).ConfigureAwait(false);
            if (state.ActiveGenerationId != plan.CurrentGenerationId)
            {
                throw Invalid("active-generation-changed", "The active ledger generation changed before activation.");
            }
        }

        private async Task<LedgerResetResult> CompleteObservedActivationAsync(
            LedgerResetPlan plan,
            LedgerResetJournal journal,
            LedgerResetGenerationState generation,
            CancellationToken cancellationToken)
        {
            if (journal.Activation is null)
            {
                var activation = new LedgerResetActivationEvidence(
                    plan.OperationId,
                    plan.CurrentGenerationId,
                    plan.NewGenerationId,
                    WasCompareAndSwapApplied: false,
                    generation.PreviousGenerationAvailable);
                journal = await _journalStore.RecordActivationAsync(
                    plan.OperationId,
                    journal.Revision,
                    activation,
                    cancellationToken).ConfigureAwait(false);
            }

            return ToResult(journal);
        }

        private async Task MarkFailedAsync(LedgerResetJournal journal, string code)
        {
            if (journal.State == LedgerResetState.Succeeded)
            {
                return;
            }

            await _journalStore.MarkFailedAsync(
                journal.OperationId,
                journal.Revision,
                code,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static void ValidatePlan(LedgerResetPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.OperationId == Guid.Empty ||
                string.IsNullOrWhiteSpace(plan.EnvironmentId) ||
                string.IsNullOrWhiteSpace(plan.LedgerId) ||
                string.IsNullOrWhiteSpace(plan.CurrentGenerationId) ||
                string.IsNullOrWhiteSpace(plan.NewGenerationId) ||
                string.IsNullOrWhiteSpace(plan.HostResourceId) ||
                string.IsNullOrWhiteSpace(plan.StorageResourceId) ||
                plan.CurrentGenerationId == plan.NewGenerationId ||
                plan.CreatedUtc.Offset != TimeSpan.Zero)
            {
                throw Invalid("reset-plan-invalid", "The ledger reset plan target is invalid.");
            }

            if (!string.Equals(
                    plan.PlanFingerprint,
                    LedgerResetPlanBinding.ComputeFingerprint(plan),
                    StringComparison.Ordinal))
            {
                throw Invalid("plan-fingerprint-mismatch", "The ledger reset plan fingerprint is stale or invalid.");
            }

            if (plan.AffectedEntityCounts is null || plan.AffectedEntityCounts.Values.Any(value => value < 0) ||
                plan.Resources is null || plan.Resources.Count == 0 ||
                plan.Resources.Any(resource =>
                    string.IsNullOrWhiteSpace(resource.ResourceId) ||
                    string.IsNullOrWhiteSpace(resource.ResourceKind) ||
                    !IsSha256(resource.OwnershipProofSha256) ||
                    (resource.IsShared && !resource.PreserveUnrelatedScopes)) ||
                plan.Resources.Select(resource => resource.ResourceId).Distinct(StringComparer.Ordinal).Count() !=
                    plan.Resources.Count ||
                !HasRequiredPreservation(plan.PreservedPolicies))
            {
                throw Invalid("reset-plan-invalid", "The reset inventory or preservation policy is invalid.");
            }
        }

        private static void ValidateBackup(LedgerResetPlan plan, LedgerResetBackupEvidence evidence)
        {
            if (evidence is null || evidence.RecoverySetId == Guid.Empty || !evidence.IsSucceeded ||
                !evidence.IsIntegrityVerified || !evidence.IsIndependentlyProtected ||
                evidence.CommonCutoffUtc.Offset != TimeSpan.Zero || evidence.CommonCutoffUtc < plan.CreatedUtc)
            {
                throw Invalid(
                    "prebackup-not-protected",
                    "Ledger reset requires a fresh integrity-verified independently protected recovery set.");
            }
        }

        private static void ValidateStageEvidence(
            LedgerResetPlan plan,
            LedgerResetStage stage,
            LedgerResetStageEvidence evidence)
        {
            if (evidence is null || evidence.Stage != stage || evidence.OperationId != plan.OperationId ||
                evidence.PlanFingerprint != plan.PlanFingerprint || evidence.LedgerId != plan.LedgerId ||
                evidence.CurrentGenerationId != plan.CurrentGenerationId ||
                evidence.NewGenerationId != plan.NewGenerationId)
            {
                throw Invalid("reset-evidence-mismatch", "Reset stage evidence does not match the immutable plan.");
            }

            if (!evidence.AffectsOnlySelectedLedger)
            {
                throw Invalid("shared-scope-violation", "A reset stage would affect unrelated shared state.");
            }

            if (!evidence.OldWorkIsolated || !evidence.RequiredResourcesReady)
            {
                throw Invalid("reset-isolation-incomplete", "Old asynchronous work or required resources are not isolated.");
            }

            if (stage == LedgerResetStage.VerifyPristine &&
                (evidence.RemainingBusinessEntityCounts is null ||
                 evidence.RemainingBusinessEntityCounts.Values.Any(value => value != 0)))
            {
                throw Invalid("target-not-pristine", "The new ledger generation is not pristine.");
            }
        }

        private static void ValidateActivation(
            LedgerResetPlan plan,
            LedgerResetActivationEvidence evidence)
        {
            if (evidence is null || evidence.OperationId != plan.OperationId ||
                evidence.PreviousGenerationId != plan.CurrentGenerationId ||
                evidence.ActiveGenerationId != plan.NewGenerationId ||
                !evidence.WasCompareAndSwapApplied || !evidence.PreviousGenerationAvailable)
            {
                throw Invalid("activation-evidence-mismatch", "Generation activation evidence is invalid.");
            }
        }

        private static void ValidateJournal(LedgerResetPlan plan, LedgerResetJournal journal)
        {
            if (journal.OperationId != plan.OperationId || journal.PlanFingerprint != plan.PlanFingerprint ||
                journal.EnvironmentId != plan.EnvironmentId || journal.LedgerId != plan.LedgerId ||
                journal.CurrentGenerationId != plan.CurrentGenerationId ||
                journal.NewGenerationId != plan.NewGenerationId || journal.Revision < 0 ||
                !IsOrderedPrefix(journal.CompletedStages) || !journal.RollbackEligible)
            {
                throw Invalid("reset-resume-binding-conflict", "Reset journal does not match the immutable plan.");
            }
        }

        private static bool IsOrderedPrefix(IReadOnlyList<LedgerResetStage> completed)
        {
            if (completed is null || completed.Count > OrderedStages.Count)
            {
                return false;
            }

            for (var index = 0; index < completed.Count; index++)
            {
                if (completed[index] != OrderedStages[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasRequiredPreservation(IReadOnlyList<string> policies)
        {
            return policies is not null &&
                policies.Contains("administrator-access", StringComparer.Ordinal) &&
                policies.Contains("deployment-configuration", StringComparer.Ordinal) &&
                policies.Contains("protected-audit", StringComparer.Ordinal);
        }

        private static bool IsSha256(string value)
        {
            return value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
        }

        private static IReadOnlyDictionary<LedgerResetStage, ILedgerResetStageAdapter> IndexAdapters(
            IEnumerable<ILedgerResetStageAdapter> adapters)
        {
            var values = adapters.ToArray();
            if (values.Any(adapter => adapter is null) ||
                values.GroupBy(adapter => adapter.Stage).Any(group => group.Count() != 1))
            {
                throw Invalid("reset-adapter-conflict", "Reset stage adapters are duplicated or invalid.");
            }

            return values.ToDictionary(adapter => adapter.Stage);
        }

        private void RequireAllAdapters()
        {
            if (_adapters.Count != OrderedStages.Count || OrderedStages.Any(stage => !_adapters.ContainsKey(stage)))
            {
                throw Invalid("reset-adapter-missing", "A required generation-isolated reset adapter is unavailable.");
            }
        }

        private static LedgerResetResult ToResult(LedgerResetJournal journal) => new(
            journal.OperationId,
            journal.State,
            journal.NewGenerationId,
            ReimportAllowed: journal.State == LedgerResetState.Succeeded,
            journal.RollbackEligible,
            Array.AsReadOnly(journal.CompletedStages.ToArray()));

        private static LedgerResetException Invalid(string code, string message) => new(code, message);
    }
}
