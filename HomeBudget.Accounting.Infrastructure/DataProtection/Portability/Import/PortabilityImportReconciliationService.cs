using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class PortabilityImportReconciliationService
    {
        private readonly PortabilityImportPreflightService _preflight;
        private readonly IPortabilityImportJournalStore _journal;
        private readonly IPortabilityImportReadbackAdapter _readback;
        private readonly PortabilityImportReconciliationOptions _options;

        public PortabilityImportReconciliationService(
            PortabilityImportPreflightService preflight,
            IPortabilityImportJournalStore journal,
            IPortabilityImportReadbackAdapter readback,
            PortabilityImportReconciliationOptions options)
        {
            _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _readback = readback ?? throw new ArgumentNullException(nameof(readback));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
        }

        public async Task<PortabilityImportReconciliationResult> ReconcileAsync(
            PortabilityImportReconciliationRequest request,
            string packageRoot,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Import);
            var journal = await _journal.GetAsync(request.OperationId, cancellationToken).ConfigureAwait(false)
                ?? throw new PortabilityImportJournalConflictException("import.journal-missing");
            var preflight = await _preflight.ValidateAsync(
                ResumeValidationRequest(request.Import, journal),
                packageRoot,
                cancellationToken).ConfigureAwait(false);
            ValidateBinding(request, preflight, journal);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ReadTimeout);
            var context = new PortabilityImportExecutionContext(
                request.Import.EnvironmentId,
                request.Import.LedgerId,
                request.Import.TargetGenerationId,
                request.Import.TargetGenerationNumber,
                journal.ImportMode);
            var actual = await _readback.ReadAsync(
                context,
                _options.MaximumRecords,
                timeout.Token).ConfigureAwait(false);
            var comparison = PortabilityImportReconciliationComparer.Compare(
                preflight.Records,
                preflight.Reconciliation,
                actual,
                _options.MaximumRecords);
            await _journal.RecordReconciliationAsync(
                new PortabilityImportReconciliationRecord(
                    request.OperationId,
                    request.ActorId,
                    comparison.Succeeded,
                    comparison.EvidenceSha256,
                    comparison.ErrorCode,
                    DateTime.UtcNow),
                timeout.Token).ConfigureAwait(false);
            return new PortabilityImportReconciliationResult(
                comparison.Succeeded ? "succeeded" : "failed",
                comparison.EvidenceSha256,
                comparison.ErrorCode,
                comparison.EntityType,
                comparison.EntityId);
        }

        private static PortabilityImportRequest ResumeValidationRequest(
            PortabilityImportRequest request,
            PortabilityImportJournal journal) => new()
        {
            Mode = "resume",
            ResumeMode = journal.ImportMode,
            ActorAuthorized = request.ActorAuthorized,
            PackageSha256 = request.PackageSha256,
            ExpectedPackageSha256 = journal.PackageSha256,
            EnvironmentId = request.EnvironmentId,
            LedgerId = request.LedgerId,
            TargetGenerationId = request.TargetGenerationId,
            TargetGenerationNumber = request.TargetGenerationNumber,
            ExpectedTargetGenerationId = journal.TargetGenerationId,
            MaintenanceIsolationConfirmed = request.MaintenanceIsolationConfirmed,
            PreOperationRecoverySetId = request.PreOperationRecoverySetId
        };

        private static void ValidateBinding(
            PortabilityImportReconciliationRequest request,
            PortabilityImportPreflightResult preflight,
            PortabilityImportJournal journal)
        {
            if (request.OperationId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.ActorId)
                || journal.PackageSha256 != preflight.PackageSha256
                || journal.TargetEnvironmentId != request.Import.EnvironmentId
                || journal.TargetLedgerId != request.Import.LedgerId
                || journal.TargetGenerationId != request.Import.TargetGenerationId
                || journal.TargetGenerationNumber != request.Import.TargetGenerationNumber)
            {
                throw new PortabilityImportJournalConflictException("import.reconciliation-binding-conflict");
            }

            if (journal.State != "awaiting-reconciliation"
                || journal.Outcomes.Any(value => value.State != "projected"))
            {
                throw new PortabilityImportJournalConflictException("import.reconciliation-state-conflict");
            }
        }
    }
}
