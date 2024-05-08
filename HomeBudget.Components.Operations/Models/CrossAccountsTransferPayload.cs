using System;

namespace HomeBudget.Components.Operations.Models
{
    public class CrossAccountsTransferPayload
    {
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public decimal Multiplier { get; set; }
        public DateOnly OperationAt { get; set; }
    }
}
