using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations
{
    public static class MockOperationsHistoryStore
    {
        private static readonly Dictionary<Guid, IEnumerable<PaymentOperationHistoryRecord>> Store = new();

        public static IReadOnlyCollection<PaymentOperationHistoryRecord> RecordsForAccount(Guid paymentAccountId) =>
            Store.TryGetValue(paymentAccountId, out var paymentOperationEventsForAccount)
                ? paymentOperationEventsForAccount.ToList()
                : Enumerable.Empty<PaymentOperationHistoryRecord>().ToList();

        public static void SetState(Guid paymentAccountId, IEnumerable<PaymentOperationHistoryRecord> payload)
        {
            Store[paymentAccountId] = payload;
        }
    }
}
