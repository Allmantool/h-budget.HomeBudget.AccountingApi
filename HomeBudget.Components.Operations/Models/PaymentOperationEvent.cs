using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core;

namespace HomeBudget.Components.Operations.Models
{
    public class PaymentOperationEvent : BaseEvent
    {
        public FinancialTransaction Payload { get; init; }

        public PaymentEventTypes EventType { get; init; }
    }
}
