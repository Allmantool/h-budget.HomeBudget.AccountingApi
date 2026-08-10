using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Extensions
{
    internal static class PaymentOperationEventExtensions
    {
        public static IReadOnlyCollection<PaymentOperationEvent> GetValidAndMostUpToDateOperations(
            this IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            var validAndMostUpToDateOperations = eventsForAccount
                .Where(ev => ev?.Payload != null)
                .GroupBy(ev => ev.Payload.Key)
                .Select(gr => gr
                    .OrderBy(ev => ev.SequenceNumber)
                    .ThenBy(ev => ev.ProcessedAt)
                    .ThenBy(ev => ev.OccurredOn)
                    .ThenBy(ev => ev.EnvelopId)
                    .Last())
                .Where(ev => ev.EventType != PaymentEventTypes.Removed)
                .ToList();

            return validAndMostUpToDateOperations
                .OrderByHistoryOrder()
                .ToList();
        }

        public static IReadOnlyList<PaymentOperationHistoryRecord> BuildHistoryRecords(
            this IReadOnlyCollection<PaymentOperationEvent> operations,
            IReadOnlyDictionary<Guid, Category> categoryMap)
        {
            var historyRecords = new List<PaymentOperationHistoryRecord>(operations.Count);
            var balance = 0m;

            foreach (var operation in operations.OrderByHistoryOrder())
            {
                balance += operation.Payload.CalculateIncrement(categoryMap);

                historyRecords.Add(new PaymentOperationHistoryRecord
                {
                    Record = operation.Payload,
                    StreamRevision = operation.SequenceNumber,
                    Balance = balance
                });
            }

            return historyRecords;
        }
    }
}
