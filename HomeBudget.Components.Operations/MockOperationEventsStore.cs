using System.Collections.Generic;
using System.Linq;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations
{
    internal static class MockOperationEventsStore
    {
        public static readonly List<PaymentOperationEvent> Events = Enumerable.Empty<PaymentOperationEvent>().ToList();
    }
}
