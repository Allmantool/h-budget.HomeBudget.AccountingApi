using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Models
{
    internal class PaymentOperationEvent
    {
        public FinancialTransaction Payload { get; set; }

        public PaymentEventTypes EventType { get; set; }
    }
}
