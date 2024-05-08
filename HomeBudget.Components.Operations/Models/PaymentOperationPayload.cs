using System;

namespace HomeBudget.Components.Operations.Models
{
    public record PaymentOperationPayload
    {
        public decimal Amount { get; set; }
        public string Comment { get; set; }
        public string CategoryId { get; set; }
        public string ContractorId { get; set; }
        public DateOnly OperationDate { get; set; }
    }
}
