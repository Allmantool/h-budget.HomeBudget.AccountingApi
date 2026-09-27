using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal sealed class PaymentOperationsHistoryService(
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ICategoryDocumentsClient categoryDocumentsClient)
        : IPaymentOperationsHistoryService
    {
        private readonly IPaymentsHistoryDocumentsClient _paymentsHistoryDocumentsClient =
            paymentsHistoryDocumentsClient ?? throw new ArgumentNullException(nameof(paymentsHistoryDocumentsClient));

        private readonly ICategoryDocumentsClient _categoryDocumentsClient =
            categoryDocumentsClient ?? throw new ArgumentNullException(nameof(categoryDocumentsClient));

        public async Task<Result<decimal>> SyncHistoryAsync(
            string financialPeriodIdentifier,
            IEnumerable<PaymentOperationEvent> eventsForAccount,
            ProjectionCheckpoint checkpoint = null,
            CancellationToken cancellationToken = default,
            Guid? completionOwnerRunId = null)
        {
            if (string.IsNullOrWhiteSpace(financialPeriodIdentifier))
            {
                throw new ArgumentException(
                    "Financial period identifier cannot be null or whitespace.",
                    nameof(financialPeriodIdentifier));
            }

            ArgumentNullException.ThrowIfNull(eventsForAccount);

            var inputEvents = eventsForAccount.ToList();
            if (inputEvents.Count == 0)
            {
                return Result<decimal>.Succeeded(0m);
            }

            var accountId = inputEvents[0].Payload.PaymentAccountId;
            var sourceRevision = inputEvents.Max(static operation => operation.SequenceNumber);
            if (checkpoint?.Revision is not null &&
                (!long.TryParse(checkpoint.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var checkpointRevision) ||
                 checkpointRevision != sourceRevision))
            {
                throw new InvalidOperationException(
                    $"Projection checkpoint revision '{checkpoint.Revision}' does not match the complete source read revision '{sourceRevision}'.");
            }

            var projectionRunId = completionOwnerRunId ?? Guid.NewGuid();
            var now = DateTime.UtcNow;
            await _paymentsHistoryDocumentsClient.BeginProjectionRunAsync(new ProjectionAuditRecord
            {
                ProjectionRunId = projectionRunId,
                StreamId = checkpoint?.StreamId ?? financialPeriodIdentifier,
                Revision = sourceRevision.ToString(CultureInfo.InvariantCulture),
                Position = checkpoint?.Position,
                Status = "Started",
                StartedUtc = now,
                UpdatedUtc = now
            });

            try
            {
                var latestActiveEvents = inputEvents
                    .GetValidAndMostUpToDateOperations()
                    .Where(static x => x?.Payload != null)
                    .ToList();

                IReadOnlyList<PaymentOperationHistoryRecord> historyRecords = latestActiveEvents.Count == 0
                    ? []
                    : inputEvents.BuildHistoryRecords(await LoadCategoryMapAsync(latestActiveEvents));
                var snapshot = new PaymentHistoryProjectionSnapshot(
                    financialPeriodIdentifier,
                    accountId,
                    checkpoint?.StreamId ?? financialPeriodIdentifier,
                    sourceRevision,
                    checkpoint?.Position,
                    PaymentHistoryProjectionFingerprint.Create(historyRecords),
                    historyRecords);
                var publication = await _paymentsHistoryDocumentsClient.PublishSnapshotAsync(
                    snapshot,
                    projectionRunId,
                    cancellationToken);
                await _paymentsHistoryDocumentsClient.RecordProjectionPublicationAsync(
                    projectionRunId,
                    publication.State.ToString());
                if (completionOwnerRunId is null)
                {
                    await _paymentsHistoryDocumentsClient.CompleteProjectionRunAsync(
                        projectionRunId,
                        "Succeeded",
                        publicationState: publication.State.ToString());
                }

                return Result<decimal>.Succeeded(snapshot.FinalBalance);
            }
            catch (Exception ex)
            {
                TelemetryMetrics.ReconciliationFailures.Add(1, [new("projection_name", "sync_operations_history")]);
                await _paymentsHistoryDocumentsClient.CompleteProjectionRunAsync(projectionRunId, "Failed", ex.Message);
                throw;
            }
        }

        private async Task<IReadOnlyDictionary<Guid, Category>> LoadCategoryMapAsync(
            IReadOnlyCollection<PaymentOperationEvent> operationEvents)
        {
            var categoryIds = operationEvents
                .Select(static x => x.Payload.CategoryId)
                .Where(static x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            if (categoryIds.Length == 0)
            {
                return new Dictionary<Guid, Category>();
            }

            var categoryDocumentsResult = await _categoryDocumentsClient.GetByIdsAsync(categoryIds);
            var categoryDocuments = categoryDocumentsResult.Payload ?? Array.Empty<CategoryDocument>();

            return categoryDocuments
                .Where(static x => x?.Payload != null)
                .GroupBy(static x => x.Payload.Key)
                .ToDictionary(static x => x.Key, static x => x.Last().Payload);
        }
    }
}
