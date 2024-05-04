using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public class CrossAccountsTransferRequest
    {
        public Guid Sender { get; set; }
        public Guid Recipient { get; set; }
        public decimal Amount { get; set; }
        public DateTime OperationAt { get; set; }
    }
}
