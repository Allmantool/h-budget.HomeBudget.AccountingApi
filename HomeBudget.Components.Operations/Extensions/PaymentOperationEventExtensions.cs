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
            var existedOperationEventGroups = eventsForAccount
                .GroupBy(ev => ev.Payload.Key)
                .Where(gr => gr.All(ev => ev.EventType != PaymentEventTypes.Removed));

            var validAndMostUpToDateOperations = existedOperationEventGroups
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.Key)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last())
                .ToList();

            return validAndMostUpToDateOperations
                    .OrderBy(ev => ev.Payload.Key)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .ToList();
        }

        public static IReadOnlyList<PaymentOperationHistoryRecord> BuildHistoryRecords(
            this IReadOnlyCollection<PaymentOperationEvent> operations,
            IReadOnlyDictionary<Guid, Category> categoryMap)
        {
            var historyRecords = new List<PaymentOperationHistoryRecord>(operations.Count);
            var balance = 0m;

            foreach (var operation in operations.Select(x => x.Payload))
            {
                balance += operation.CalculateIncrement(categoryMap);

                historyRecords.Add(new PaymentOperationHistoryRecord
                {
                    Record = operation,
                    Balance = balance
                });
            }

            return historyRecords;
        }
    }
}
