using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations
{
    public static class MockOperationsHistoryStore
    {
        public static IEnumerable<PaymentOperationHistoryRecord> Records { get; private set; }
            = Enumerable.Empty<PaymentOperationHistoryRecord>();

        public static void SetState(IEnumerable<PaymentOperationHistoryRecord> payload)
        {
            Records = payload;
        }
    }
}
