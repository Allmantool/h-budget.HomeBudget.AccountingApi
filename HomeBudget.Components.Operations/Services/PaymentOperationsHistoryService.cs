using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService(
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ICategoryDocumentsClient categoryDocumentsClient)
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
                await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);
                return new Result<decimal>();
            }

            if (validAndMostUpToDateOperations.Count == 1)
            {
                var paymentOperation = validAndMostUpToDateOperations.Single().Payload;

                var record = new PaymentOperationHistoryRecord
                {
                    Record = paymentOperation,
                    Balance = await CalculateIncrementAsync(paymentOperation)
                };

                await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);
                await paymentsHistoryDocumentsClient.InsertOneAsync(paymentAccountId, record);

                return new Result<decimal>(record.Balance);
            }

            await InsertManyAsync(paymentAccountId, validAndMostUpToDateOperations);

            var historyRecords = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);

            var historyOperationDocument = historyRecords.Any()
                ? historyRecords.Last()
                : default;

            return new Result<decimal>(historyOperationDocument?.Payload.Balance ?? 0);
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
                        Balance = previousRecordBalance + await CalculateIncrementAsync(operationEvent)
                    });
            }

            await paymentsHistoryDocumentsClient.RewriteAllAsync(paymentAccountId, operationsHistory);
        }

        private async Task<decimal> CalculateIncrementAsync(PaymentOperation operation)
        {
            var documentResult = await categoryDocumentsClient.GetByIdAsync(operation.CategoryId);
            var documentPayload = documentResult.Payload;
            var category = documentPayload.Payload;

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
                .Where(gr => gr.Any(ev => ev.EventType == PaymentEventTypes.Added));

            return existedOperationEventGroups
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.OperationDay)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last())
                .ToList();
        }
    }
}
