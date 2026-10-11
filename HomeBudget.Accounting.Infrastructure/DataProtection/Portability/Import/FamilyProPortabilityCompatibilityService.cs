using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class FamilyProPortabilityCompatibilityService
    {
        private readonly PortabilityImportPreflightService _preflight;
        private readonly IPortabilityImportJournalStore _journal;

        public FamilyProPortabilityCompatibilityService(
            PortabilityImportPreflightService preflight,
            IPortabilityImportJournalStore journal)
        {
            _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        public async Task<FamilyProPortabilityHandoff> ResumeAsync(
            FamilyProResumeRequest request,
            string packageRoot,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateReferences(
                request.ExternalJournalId,
                request.SourceBackupReference,
                request.ApprovedManifestReference);
            var journal = await RequireJournalAsync(request.OperationId, cancellationToken).ConfigureAwait(false);
            if (request.Import == null
                || request.Import.Mode != "resume"
                || request.Import.TargetGenerationId != journal.TargetGenerationId
                || request.Import.TargetGenerationNumber != journal.TargetGenerationNumber)
            {
                throw Conflict("familypro.resume-generation-conflict");
            }

            var import = ResumeImport(request.Import, journal);
            var preflight = await _preflight.ValidateAsync(
                import,
                packageRoot,
                cancellationToken).ConfigureAwait(false);
            EnsurePackageBinding(journal, preflight);
            return Handoff(
                new PortabilityImportExecutionRequest
                {
                    OperationId = request.OperationId,
                    ActorId = request.ActorId,
                    Import = import
                },
                request.OperationId,
                request.ExternalJournalId,
                request.ExternalJournalId,
                request.SourceBackupReference,
                request.ApprovedManifestReference,
                preflight.Records);
        }

        public async Task<FamilyProPortabilityHandoff> PrepareRemigrationAsync(
            FamilyProRemigrationRequest request,
            string packageRoot,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateReferences(
                request.PriorExternalJournalId,
                request.NewExternalJournalId,
                request.SourceBackupReference,
                request.ApprovedManifestReference);
            var prior = await RequireJournalAsync(request.PriorOperationId, cancellationToken).ConfigureAwait(false);
            if (request.Import == null
                || request.Import.Mode != "empty-target"
                || request.NewOperationId == Guid.Empty
                || request.NewOperationId == request.PriorOperationId
                || request.NewExternalJournalId == request.PriorExternalJournalId
                || request.Import.TargetGenerationId == prior.TargetGenerationId
                || request.Import.TargetGenerationNumber == prior.TargetGenerationNumber)
            {
                throw Conflict("familypro.remigration-identity-conflict");
            }

            var preflight = await _preflight.ValidateAsync(
                request.Import,
                packageRoot,
                cancellationToken).ConfigureAwait(false);
            EnsurePackageBinding(prior, preflight);
            return Handoff(
                new PortabilityImportExecutionRequest
                {
                    OperationId = request.NewOperationId,
                    ActorId = request.ActorId,
                    Import = request.Import
                },
                request.PriorOperationId,
                request.PriorExternalJournalId,
                request.NewExternalJournalId,
                request.SourceBackupReference,
                request.ApprovedManifestReference,
                preflight.Records);
        }

        private async Task<PortabilityImportJournal> RequireJournalAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            await _journal.GetAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw Conflict("familypro.prior-journal-missing");

        private static PortabilityImportRequest ResumeImport(
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

        private static FamilyProPortabilityHandoff Handoff(
            PortabilityImportExecutionRequest execution,
            Guid priorOperationId,
            string priorExternalJournalId,
            string externalJournalId,
            string sourceBackupReference,
            string approvedManifestReference,
            IReadOnlyList<PortabilityImportRecord> records)
        {
            var lineage = records.Where(value => value.EntityType == "lineage")
                .Select(Lineage).ToArray();
            if (lineage.Length == 0)
            {
                throw Conflict("familypro.lineage-contract-invalid");
            }

            var legacyReferences = records
                .Where(value => value.Payload.TryGetProperty("legacyReference", out var reference)
                    && reference.ValueKind == JsonValueKind.String)
                .Select(value => new FamilyProLegacyReference(
                    value.EntityType,
                    value.SourceEntityId,
                    value.Payload.GetProperty("legacyReference").GetString()))
                .ToArray();
            return new FamilyProPortabilityHandoff(
                execution,
                priorOperationId,
                priorExternalJournalId,
                externalJournalId,
                sourceBackupReference,
                approvedManifestReference,
                lineage,
                legacyReferences);
        }

        private static FamilyProLineageIdentity Lineage(PortabilityImportRecord record)
        {
            var value = record.Payload;
            if (value.GetProperty("sourceSystem").GetString() != "FamilyPro12")
            {
                throw Conflict("familypro.lineage-contract-invalid");
            }

            return new FamilyProLineageIdentity(
                record.SourceEntityId,
                value.GetProperty("entityType").GetString(),
                value.GetProperty("entityId").GetString(),
                value.GetProperty("sourceEntityId").GetString(),
                value.GetProperty("sourceRunId").GetString(),
                Optional(value, "sourceBatchId"),
                Optional(value, "mappingId"));
        }

        private static string Optional(JsonElement value, string property) =>
            value.GetProperty(property).ValueKind == JsonValueKind.Null
                ? null
                : value.GetProperty(property).GetString();

        private static void EnsurePackageBinding(
            PortabilityImportJournal journal,
            PortabilityImportPreflightResult preflight)
        {
            if (journal.PackageSha256 != preflight.PackageSha256
                || journal.ExportId != preflight.ExportId)
            {
                throw Conflict("familypro.source-binding-conflict");
            }
        }

        private static void ValidateReferences(params string[] references)
        {
            if (references.Any(value => string.IsNullOrWhiteSpace(value)
                || value.Length > 256
                || value.Any(char.IsControl)))
            {
                throw Conflict("familypro.reference-invalid");
            }
        }

        private static PortabilityImportJournalConflictException Conflict(string code) => new(code);
    }
}
