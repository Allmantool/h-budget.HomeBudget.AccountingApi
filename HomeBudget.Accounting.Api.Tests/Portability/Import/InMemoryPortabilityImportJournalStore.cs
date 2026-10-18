using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class InMemoryPortabilityImportJournalStore : IPortabilityImportJournalStore
    {
        private readonly Dictionary<Guid, PortabilityImportJournal> _journals = [];

        public Task<PortabilityImportJournal> CreateOrReadAsync(
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_journals.TryGetValue(request.OperationId, out var existing))
            {
                if (existing.PackageSha256 != request.Preflight.PackageSha256
                    || existing.ExportId != request.Preflight.ExportId
                    || existing.TargetGenerationId != request.TargetGenerationId
                    || existing.ImportMode != request.Mode
                    || !SameEntities(existing.Outcomes, request.Preflight.Entities))
                {
                    throw new PortabilityImportJournalConflictException("import.journal-binding-conflict");
                }

                return Task.FromResult(existing);
            }

            var journal = new PortabilityImportJournal
            {
                OperationId = request.OperationId,
                ExportId = request.Preflight.ExportId,
                PackageSha256 = request.Preflight.PackageSha256,
                FormatVersion = request.Preflight.FormatVersion,
                Completeness = request.Preflight.Completeness,
                ImportMode = request.Mode,
                TargetEnvironmentId = request.TargetEnvironmentId,
                TargetLedgerId = request.TargetLedgerId,
                TargetGenerationId = request.TargetGenerationId,
                TargetGenerationNumber = request.TargetGenerationNumber,
                PreOperationRecoverySetId = request.PreOperationRecoverySetId,
                EntityCount = request.Preflight.Entities.Count,
                State = "journaled",
                CreatedUtc = request.CreatedUtc,
                UpdatedUtc = request.CreatedUtc,
                Outcomes = request.Preflight.Entities.Select(value => new PortabilityImportOutcome
                {
                    ImportOutcomeId = Guid.NewGuid(),
                    OperationId = request.OperationId,
                    EntityType = value.EntityType,
                    SourceEntityId = value.SourceEntityId,
                    TargetEntityId = value.SourceEntityId,
                    IdempotencyKey = value.IdempotencyKey,
                    Fingerprint = value.Fingerprint,
                    State = "validated",
                    CreatedUtc = request.CreatedUtc,
                    UpdatedUtc = request.CreatedUtc
                }).ToArray()
            };
            _journals.Add(request.OperationId, journal);
            return Task.FromResult(journal);
        }

        public Task<PortabilityImportJournal> GetAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _journals.TryGetValue(operationId, out var journal);
            return Task.FromResult(journal);
        }

        public Task<bool> TryTransitionOutcomeAsync(
            PortabilityImportOutcomeTransition transition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journal = _journals[transition.OperationId];
            var outcome = journal.Outcomes.Single(value =>
                value.EntityType == transition.EntityType
                && value.SourceEntityId == transition.SourceEntityId);
            if (outcome.Fingerprint != transition.Fingerprint
                || outcome.IdempotencyKey != transition.IdempotencyKey)
            {
                throw new PortabilityImportJournalConflictException("import.outcome-state-conflict");
            }

            if (outcome.State == transition.NextState)
            {
                return Task.FromResult(false);
            }

            if (outcome.State != transition.ExpectedState)
            {
                throw new PortabilityImportJournalConflictException("import.outcome-state-conflict");
            }

            outcome.State = transition.NextState;
            outcome.ErrorClassification = transition.ErrorClassification;
            outcome.UpdatedUtc = transition.UpdatedUtc;
            return Task.FromResult(true);
        }

        public Task<bool> TryTransitionRunAsync(
            Guid operationId,
            string expectedState,
            string nextState,
            DateTime updatedUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journal = _journals[operationId];
            if (journal.State == nextState)
            {
                return Task.FromResult(false);
            }

            if (journal.State != expectedState)
            {
                throw new PortabilityImportJournalConflictException("import.run-state-conflict");
            }

            journal.State = nextState;
            journal.UpdatedUtc = updatedUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordReconciliationAsync(
            PortabilityImportReconciliationRecord record,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journal = _journals[record.OperationId];
            var nextState = record.Succeeded ? "succeeded" : "failed";
            if (journal.State == nextState)
            {
                return Task.FromResult(false);
            }

            if (journal.State != "awaiting-reconciliation")
            {
                throw new PortabilityImportJournalConflictException("import.reconciliation-state-conflict");
            }

            if (record.Succeeded)
            {
                if (journal.Outcomes.Any(value => value.State != "projected"))
                {
                    throw new PortabilityImportJournalConflictException("import.reconciliation-state-conflict");
                }

                foreach (var outcome in journal.Outcomes)
                {
                    outcome.State = "reconciled";
                    outcome.UpdatedUtc = record.OccurredUtc;
                }
            }

            journal.State = nextState;
            journal.UpdatedUtc = record.OccurredUtc;
            return Task.FromResult(true);
        }

        private static bool SameEntities(
            IReadOnlyList<PortabilityImportOutcome> outcomes,
            IReadOnlyList<PortabilityImportEntityBinding> entities)
        {
            var expected = entities.ToDictionary(
                value => (value.EntityType, value.SourceEntityId),
                value => (value.Fingerprint, value.IdempotencyKey));
            return outcomes.Count == expected.Count && outcomes.All(value =>
                expected.TryGetValue((value.EntityType, value.SourceEntityId), out var binding)
                && binding.Fingerprint == value.Fingerprint
                && binding.IdempotencyKey == value.IdempotencyKey);
        }
    }
}
