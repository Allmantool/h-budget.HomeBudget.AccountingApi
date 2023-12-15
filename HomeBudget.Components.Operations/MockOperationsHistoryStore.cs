using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations
{
    public static class MockOperationsHistoryStore
    {
        private static readonly Dictionary<Guid, IEnumerable<PaymentOperationHistoryRecord>> Store = new();

        public static IEnumerable<PaymentOperationHistoryRecord> Records { get; private set; }
            = Store.Values.SelectMany(i => i);

        public static void SetState(Guid paymentAccountId, IEnumerable<PaymentOperationHistoryRecord> payload)
        {
            Store[paymentAccountId] = payload;
        }
    }
}
