using System;

namespace HomeBudget.Components.Operations.Models
{
    public record RemoveTransferPayload
    {
        public Guid PaymentAccountId { get; init; }
        public Guid TransferOperationId { get; init; }
    }
}
