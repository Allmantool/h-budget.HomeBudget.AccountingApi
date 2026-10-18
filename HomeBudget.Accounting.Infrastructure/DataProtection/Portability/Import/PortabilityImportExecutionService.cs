using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed partial class PortabilityImportExecutionService
    {
        private readonly PortabilityImportPreflightService _preflight;
        private readonly IPortabilityImportJournalStore _journal;
        private readonly IPortabilityImportApplicationAdapter _adapter;
        private readonly PortabilityImportExecutionOptions _options;

        public PortabilityImportExecutionService(
            PortabilityImportPreflightService preflight,
            IPortabilityImportJournalStore journal,
            IPortabilityImportApplicationAdapter adapter,
            PortabilityImportExecutionOptions options)
        {
            _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
        }

        public async Task<PortabilityImportExecutionResult> ExecuteAsync(
            PortabilityImportExecutionRequest request,
            string packageRoot,
            CancellationToken cancellationToken)
        {
            ValidateExecutionRequest(request);
            var preflight = await _preflight.ValidateAsync(
                request.Import,
                packageRoot,
                cancellationToken).ConfigureAwait(false);
            var mode = EffectiveMode(request.Import);
            var journal = await _journal.CreateOrReadAsync(
                CreateJournalRequest(request, preflight, mode),
                cancellationToken).ConfigureAwait(false);
            if (journal.State is "awaiting-reconciliation" or "cancelled")
            {
                return Result(journal);
            }

            if (journal.State == "journaled")
            {
                await _journal.TryTransitionRunAsync(
                    request.OperationId,
                    "journaled",
                    "executing",
                    DateTime.UtcNow,
                    cancellationToken).ConfigureAwait(false);
            }

            var context = new PortabilityImportExecutionContext(
                request.Import.EnvironmentId,
                request.Import.LedgerId,
                request.Import.TargetGenerationId,
                request.Import.TargetGenerationNumber,
                mode);
            var cancelled = await ExecuteRecordsAsync(
                request.OperationId,
                preflight.Records,
                context,
                cancellationToken).ConfigureAwait(false);
            using var finalizationTimeout = new CancellationTokenSource(_options.SettlementTimeout);
            journal = await RequireJournalAsync(
                request.OperationId,
                finalizationTimeout.Token).ConfigureAwait(false);
            var nextState = cancelled ? "cancelled" : "awaiting-reconciliation";
            if (!cancelled && journal.Outcomes.Any(value => value.State != "projected"))
            {
                throw PortabilityImportExecutionException.Create("import.settlement-incomplete");
            }

            await _journal.TryTransitionRunAsync(
                request.OperationId,
                "executing",
                nextState,
                DateTime.UtcNow,
                finalizationTimeout.Token).ConfigureAwait(false);
            return Result(await RequireJournalAsync(
                request.OperationId,
                finalizationTimeout.Token).ConfigureAwait(false));
        }

        private static void ValidateExecutionRequest(PortabilityImportExecutionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Import);
            if (request.OperationId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.ActorId)
                || request.Import.Mode is not "empty-target" and not "replace" and not "resume")
            {
                throw new ArgumentException("A complete executable import request is required.", nameof(request));
            }
        }

        private static string EffectiveMode(PortabilityImportRequest request) =>
            request.Mode == "resume" ? request.ResumeMode : request.Mode;

        private static PortabilityImportJournalRequest CreateJournalRequest(
            PortabilityImportExecutionRequest request,
            PortabilityImportPreflightResult preflight,
            string mode) => new()
        {
            OperationId = request.OperationId,
            ActorId = request.ActorId,
            Mode = mode,
            TargetEnvironmentId = request.Import.EnvironmentId,
            TargetLedgerId = request.Import.LedgerId,
            TargetGenerationId = request.Import.TargetGenerationId,
            TargetGenerationNumber = request.Import.TargetGenerationNumber,
            PreOperationRecoverySetId = Guid.TryParse(
                request.Import.PreOperationRecoverySetId,
                out var recoverySetId) ? recoverySetId : null,
            CreatedUtc = DateTime.UtcNow,
            Preflight = preflight
        };

        private async Task<PortabilityImportJournal> RequireJournalAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            await _journal.GetAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new PortabilityImportJournalConflictException("import.journal-missing");

        private static PortabilityImportExecutionResult Result(PortabilityImportJournal journal)
        {
            var accepted = journal.Outcomes.Count(value => value.State is "accepted" or "persisted" or "projected");
            var settled = journal.Outcomes.Count(value => value.State == "projected");
            return new PortabilityImportExecutionResult(
                journal.State,
                accepted,
                settled,
                journal.Outcomes.Count - settled);
        }
    }
}
