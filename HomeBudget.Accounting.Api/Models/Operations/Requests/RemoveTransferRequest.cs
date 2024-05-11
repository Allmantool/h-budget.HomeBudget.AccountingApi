using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record RemoveTransferRequest
    {
        public Guid PaymentAccountId { get; set; }
        public Guid TransferOperationId { get; set; }
    }
}
