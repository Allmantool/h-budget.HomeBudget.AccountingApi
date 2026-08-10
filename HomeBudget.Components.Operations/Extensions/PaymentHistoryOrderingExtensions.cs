using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Extensions
{
    public static class PaymentHistoryOrderingExtensions
    {
        public static IOrderedEnumerable<PaymentOperationHistoryRecord> OrderByHistoryOrder(
            this IEnumerable<PaymentOperationHistoryRecord> operations)
        {
            return operations
                .OrderBy(static operation => operation.Record.OperationDay)
                .ThenBy(static operation => operation.Record.OperationUnixTime)
                .ThenBy(static operation => operation.StreamRevision ?? long.MaxValue)
                .ThenBy(static operation => operation.Record.Key);
        }

        public static IOrderedEnumerable<PaymentHistoryDocument> OrderByHistoryOrder(
            this IEnumerable<PaymentHistoryDocument> documents)
        {
            return documents
                .OrderBy(static document => document.Payload.Record.OperationDay)
                .ThenBy(static document => document.Payload.Record.OperationUnixTime)
                .ThenBy(static document => document.Payload.StreamRevision ?? long.MaxValue)
                .ThenBy(static document => document.Payload.Record.Key);
        }
    }
}

