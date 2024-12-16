using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Models
{
    public record PaymentOperationEvent
    {
        public FinancialTransaction Payload { get; init; }

        public PaymentEventTypes EventType { get; init; }
    }
}
