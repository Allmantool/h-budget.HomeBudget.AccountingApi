using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Extensions
{
    public static class PaymentHistoryOrderingExtensions
    {
        public static IOrderedEnumerable<PaymentOperationEvent> OrderByHistoryOrder(
            this IEnumerable<PaymentOperationEvent> operations)
        {
            return operations
                .OrderBy(static operation => operation.Payload.OperationDay)
                .ThenBy(static operation => operation.Payload.OperationUnixTime)
                .ThenBy(static operation => operation.SequenceNumber)
                .ThenBy(static operation => operation.Payload.Key);
        }

        public static IOrderedEnumerable<PaymentOperationHistoryRecord> OrderByHistoryOrder(
            this IEnumerable<PaymentOperationHistoryRecord> operations)
        {
            return operations
                .OrderBy(static operation => operation.Record.OperationDay)
                .ThenBy(static operation => operation.Record.OperationUnixTime)
                .ThenBy(static operation => operation.StreamRevision ?? long.MaxValue)
                .ThenBy(static operation => operation.Record.Key);
        }

        public static IEnumerable<PaymentOperationHistoryRecord> GetMostRecentByOperationKey(
            this IEnumerable<PaymentOperationHistoryRecord> operations)
        {
            return operations
                .GroupBy(static operation => operation.Record.Key)
                .Select(static group => group
                    .OrderBy(static operation => operation.StreamRevision.HasValue)
                    .ThenBy(static operation => operation.StreamRevision)
                    .ThenBy(static operation => operation.Record.OperationDay)
                    .ThenBy(static operation => operation.Record.OperationUnixTime)
                    .ThenBy(static operation => operation.Record.Key)
                    .Last());
        }
    }
}

