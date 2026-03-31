using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

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
            IEnumerable<PaymentOperationEvent> eventsForAccount)
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

            var latestActiveEvents = inputEvents
                .GetValidAndMostUpToDateOperations()
                .Where(static x => x?.Payload != null)
                .ToList();

            if (latestActiveEvents.Count == 0)
            {
                await _paymentsHistoryDocumentsClient.RemoveAsync(financialPeriodIdentifier);
                return Result<decimal>.Succeeded(0m);
            }

            var categoryMap = await LoadCategoryMapAsync(latestActiveEvents);
            var historyRecords = latestActiveEvents.BuildHistoryRecords(categoryMap);

            await _paymentsHistoryDocumentsClient.BulkWriteAsync(financialPeriodIdentifier, historyRecords);

            return Result<decimal>.Succeeded(historyRecords[^1].Balance);
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