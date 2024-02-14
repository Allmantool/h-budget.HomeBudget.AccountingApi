using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Models
{
    internal class PaymentOperationEvent
    {
        public PaymentOperation Payload { get; set; }

        public PaymentEventTypes EventType { get; set; }
    }
}
