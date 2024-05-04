using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record CrossAccountsTransferRequest
    {
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public decimal Multiplier { get; set; }
        public DateOnly OperationAt { get; set; }
    }
}
