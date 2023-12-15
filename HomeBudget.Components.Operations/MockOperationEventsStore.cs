using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations
{
    internal static class MockOperationEventsStore
    {
        private static readonly ConcurrentDictionary<Guid, IEnumerable<PaymentOperationEvent>> Store = new();

        public static IReadOnlyCollection<PaymentOperationEvent> EventsForAccount(Guid paymentAccountId) =>
            Store.TryGetValue(paymentAccountId, out var paymentOperationEventsForAccount)
            ? paymentOperationEventsForAccount.ToList()
            : Enumerable.Empty<PaymentOperationEvent>().ToList();

        public static void SetState(Guid paymentAccountId, IEnumerable<PaymentOperationEvent> payload)
        {
            Store[paymentAccountId] = payload;
        }
    }
}
