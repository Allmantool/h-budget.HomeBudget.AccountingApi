using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations
{
    internal static class MockOperationEventsStore
    {
        private static readonly Dictionary<Guid, IEnumerable<PaymentOperationEvent>> Store = new();

        public static IEnumerable<PaymentOperationEvent> Events { get; private set; }
            = Store.Values.SelectMany(i => i);

        public static IEnumerable<PaymentOperationEvent> EventsForAccount(Guid paymentAccountId) =>
            Store.TryGetValue(paymentAccountId, out var paymentOperationEventsForAccount)
            ? paymentOperationEventsForAccount
            : Enumerable.Empty<PaymentOperationEvent>();

        public static void SetState(Guid paymentAccountId, IEnumerable<PaymentOperationEvent> payload)
        {
            Store[paymentAccountId] = payload;
        }
    }
}
