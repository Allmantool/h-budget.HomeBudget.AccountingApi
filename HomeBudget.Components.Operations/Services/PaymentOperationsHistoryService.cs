﻿using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService : IPaymentOperationsHistoryService
    {
        public Result<decimal> SyncHistory(Guid paymentAccountId)
        {
            var eventsForAccount = MockOperationEventsStore.EventsForAccount(paymentAccountId).ToList();

            if (!eventsForAccount.Any())
            {
                MockOperationsHistoryStore.SetState(paymentAccountId, Enumerable.Empty<PaymentOperationHistoryRecord>());
                return new Result<decimal>();
            }

            var validAndMostUpToDateOperations = GetValidAndMostUpToDateOperations(eventsForAccount)
                .OrderBy(ev => ev.Payload.OperationDay)
                .ThenBy(ev => ev.Payload.OperationUnixTime)
                .ToList();

            if (!validAndMostUpToDateOperations.Any())
            {
                MockOperationsHistoryStore.SetState(paymentAccountId, Enumerable.Empty<PaymentOperationHistoryRecord>());
                return new Result<decimal>();
            }

            if (validAndMostUpToDateOperations.Count == 1)
            {
                var paymentOperation = validAndMostUpToDateOperations.Single().Payload;

                MockOperationsHistoryStore.SetState(
                    paymentAccountId,
                    new[]
                    {
                        new PaymentOperationHistoryRecord
                        {
                            Record = paymentOperation,
                            Balance = CalculateIncrement(paymentOperation)
                        }
                    });

                return new Result<decimal>(MockOperationsHistoryStore.RecordsForAccount(paymentAccountId).Last().Balance);
            }

            var operationsHistory = new List<PaymentOperationHistoryRecord>();

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

            MockOperationsHistoryStore.SetState(paymentAccountId, operationsHistory);

            var historyOperationRecord = MockOperationsHistoryStore.RecordsForAccount(paymentAccountId).Any()
                ? MockOperationsHistoryStore.RecordsForAccount(paymentAccountId).Last()
                : default;

            return new Result<decimal>(historyOperationRecord?.Balance ?? 0);
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
