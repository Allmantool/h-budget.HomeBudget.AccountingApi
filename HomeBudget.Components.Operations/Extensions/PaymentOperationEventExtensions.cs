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

            return validAndMostUpToDateOperations;
        }

        public static IReadOnlyList<PaymentOperationHistoryRecord> BuildHistoryRecords(
            this IEnumerable<PaymentOperationEvent> eventsForAccount,
            IReadOnlyDictionary<Guid, Category> categoryMap)
        {
            var historyRecords = eventsForAccount
                .Where(static operation => operation?.Payload != null)
                .GroupBy(static operation => operation.Payload.Key)
                .Select(static group => new
                {
                    Latest = group
                        .OrderBy(static operation => operation.SequenceNumber)
                        .ThenBy(static operation => operation.ProcessedAt)
                        .ThenBy(static operation => operation.OccurredOn)
                        .ThenBy(static operation => operation.EnvelopId)
                        .Last(),
                    CreationRevision = group
                        .Where(static operation => operation.EventType == PaymentEventTypes.Added)
                        .OrderBy(static operation => operation.SequenceNumber)
                        .Select(static operation => (long?)operation.SequenceNumber)
                        .FirstOrDefault()
                })
                .Where(static operation => operation.Latest.EventType != PaymentEventTypes.Removed)
                .Select(static operation => new PaymentOperationHistoryRecord
                {
                    Record = operation.Latest.Payload,
                    StreamRevision = operation.CreationRevision ?? operation.Latest.SequenceNumber
                })
                .OrderByHistoryOrder()
                .ToList();
            var balance = 0m;

            foreach (var operation in historyRecords)
            {
                balance += operation.Record.CalculateIncrement(categoryMap);
                operation.Balance = balance;
            }

            return historyRecords;
        }
    }
}
