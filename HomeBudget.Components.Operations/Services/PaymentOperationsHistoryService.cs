using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Providers;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService(
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient)
        : IPaymentOperationsHistoryService
    {
        public async Task<Result<decimal>> SyncHistoryAsync(Guid paymentAccountId)
        {
            var eventsForAccount = await eventStoreDbClient.ReadAsync(paymentAccountId.ToString()).ToListAsync();

            if (!eventsForAccount.Any())
            {
                return new Result<decimal>();
            }

            var validAndMostUpToDateOperations = GetValidAndMostUpToDateOperations(eventsForAccount)
                .OrderBy(ev => ev.Payload.OperationDay)
                .ThenBy(ev => ev.Payload.OperationUnixTime)
                .ToList();

            if (!validAndMostUpToDateOperations.Any())
            {
                return new Result<decimal>();
            }

            if (validAndMostUpToDateOperations.Count == 1)
            {
                var paymentOperation = validAndMostUpToDateOperations.Single().Payload;

                var record = new PaymentOperationHistoryRecord
                {
                    Record = paymentOperation,
                    Balance = CalculateIncrement(paymentOperation)
                };

                await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);
                await paymentsHistoryDocumentsClient.InsertOneAsync(paymentAccountId, record);

                return new Result<decimal>(record.Balance);
            }

            await InsertManyAsync(paymentAccountId, validAndMostUpToDateOperations);

            var historyRecords = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);

            var historyOperationRecord = historyRecords.Any()
                ? historyRecords.Last()
                : default;

            return new Result<decimal>(historyOperationRecord?.Balance ?? 0);
        }

        private async Task InsertManyAsync(
            Guid paymentAccountId,
            IEnumerable<PaymentOperationEvent> validAndMostUpToDateOperations)
        {
            var operationsHistory = new List<PaymentOperationHistoryRecord>();

            await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);

            foreach (var operationEvent in validAndMostUpToDateOperations.Select(r => r.Payload))
            {
                var previousRecordBalance = operationsHistory.Any()
                    ? operationsHistory.Last().Balance
                    : 0;

                operationsHistory.Add(
                    new PaymentOperationHistoryRecord
                    {
                        Record = operationEvent,
                        Balance = previousRecordBalance + CalculateIncrement(operationEvent)
                    });
            }

            await paymentsHistoryDocumentsClient.RewriteAllAsync(paymentAccountId, operationsHistory);
        }

        private static decimal CalculateIncrement(PaymentOperation operation)
        {
            var category = MockCategoriesStore.Categories.Find(c => c.Key.CompareTo(operation.CategoryId) == 0);

            return category.CategoryType == CategoryTypes.Income
                ? Math.Abs(operation.Amount)
                : -Math.Abs(operation.Amount);
        }

        private static IReadOnlyCollection<PaymentOperationEvent> GetValidAndMostUpToDateOperations(
            IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            var existedOperationEventGroups = eventsForAccount
                .GroupBy(ev => ev.Payload.Key)
                .Where(gr => gr.All(ev => ev.EventType != EventTypes.Remove))
                .Where(gr => gr.Any(ev => ev.EventType == EventTypes.Add));

            return existedOperationEventGroups
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.OperationDay)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last())
                .ToList();
        }
    }
}
