using System;

namespace HomeBudget.Components.Operations.Models
{
    public record RemoveTransferPayload
    {
        public Guid PaymentAccountId { get; set; }
        public Guid TransferOperationId { get; set; }
    }
}
