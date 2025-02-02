using System;

namespace HomeBudget.Components.Operations.Models
{
    public record PaymentOperationPayload
    {
        public int ScopeOperationId { get; init; }
        public decimal Amount { get; init; }
        public string Comment { get; init; }
        public string CategoryId { get; init; }
        public string ContractorId { get; init; }
        public DateOnly OperationDate { get; init; }
    }
}
