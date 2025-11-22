using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService(
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ICategoryDocumentsClient categoryDocumentsClient)
    : IPaymentOperationsHistoryService
    {
        public async Task<Result<decimal>> SyncHistoryAsync(
            string financialPeriodIdentifier,
            IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            if (eventsForAccount.IsNullOrEmpty())
            {
                return Result<decimal>.Succeeded(default);
            }

            var validAndMostUpToDateOperations = GetValidAndMostUpToDateOperations(eventsForAccount)
                .OrderBy(ev => ev.Payload.OperationDay)
                .ThenBy(ev => ev.Payload.OperationUnixTime)
                .ToList();

            if (validAndMostUpToDateOperations.IsNullOrEmpty())
            {
                await paymentsHistoryDocumentsClient.RemoveAsync(financialPeriodIdentifier);
                return Result<decimal>.Succeeded(0);
            }

            if (validAndMostUpToDateOperations.Count == 1)
            {
                var paymentOperation = validAndMostUpToDateOperations.Single().Payload;

                var categoryDocumentsResult = await categoryDocumentsClient.GetByIdAsync(paymentOperation.CategoryId);
                var categoryDocument = categoryDocumentsResult.Payload;

                var categoryMap = categoryDocument == null
                    ? new Dictionary<Guid, Category>()
                    : new Dictionary<Guid, Category>
                    {
                        { categoryDocument.Payload.Key, categoryDocument.Payload }
                    };

                var record = new PaymentOperationHistoryRecord
                {
                    Record = paymentOperation,
                    Balance = CalculateIncrement(paymentOperation, categoryMap)
                };

                await paymentsHistoryDocumentsClient.ReplaceOneAsync(financialPeriodIdentifier, record);
                return Result<decimal>.Succeeded(record.Balance);
            }

            await InsertManyAsync(financialPeriodIdentifier, validAndMostUpToDateOperations);

            var mostUpToDateHistoryDocument = await paymentsHistoryDocumentsClient.GetLastForPeriodAsync(financialPeriodIdentifier);
            var mostUpToDateHistoryRecord = mostUpToDateHistoryDocument?.Payload;

            return Result<decimal>.Succeeded(mostUpToDateHistoryRecord?.Balance ?? 0);
        }

        private async Task InsertManyAsync(
            string financialPeriodIdentifier,
            IEnumerable<PaymentOperationEvent> validAndMostUpToDateOperations)
        {
            var categoryIds = validAndMostUpToDateOperations
                .Select(op => op.Payload.CategoryId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            var categoryDocumentsResult = await categoryDocumentsClient.GetByIdsAsync(categoryIds);
            var categoryMap = categoryDocumentsResult.Payload.ToDictionary(doc => doc.Payload.Key, doc => doc.Payload);

            var operationsHistory = new List<PaymentOperationHistoryRecord>();
            decimal previousBalance = 0;

            foreach (var operationEvent in validAndMostUpToDateOperations.Select(r => r.Payload))
            {
                var increment = CalculateIncrement(operationEvent, categoryMap);
                previousBalance += increment;

                operationsHistory.Add(new PaymentOperationHistoryRecord
                {
                    Record = operationEvent,
                    Balance = previousBalance
                });
            }

            await paymentsHistoryDocumentsClient.BulkWriteAsync(financialPeriodIdentifier, operationsHistory);
        }

        private static decimal CalculateIncrement(FinancialTransaction operation, Dictionary<Guid, Category> categoryMap)
        {
            if (operation.CategoryId == Guid.Empty)
            {
                return operation.Amount;
            }

            if (!categoryMap.TryGetValue(operation.CategoryId, out var category))
            {
                category = new Category(CategoryTypes.Expense, ["with empty category"]);
            }

            return category.CategoryType == CategoryTypes.Income
                ? Math.Abs(operation.Amount)
                : -Math.Abs(operation.Amount);
        }

        private static IReadOnlyCollection<PaymentOperationEvent> GetValidAndMostUpToDateOperations(
            IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            var existedOperationEventGroups = eventsForAccount
                .GroupBy(ev => ev.Payload.Key)
                .Where(gr => gr.All(ev => ev.EventType != PaymentEventTypes.Removed))
                .Where(gr => gr.Any(ev => ev.EventType is PaymentEventTypes.Added or PaymentEventTypes.Updated));

            return existedOperationEventGroups
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.OperationDay)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last())
                .ToList();
        }
    }
}
