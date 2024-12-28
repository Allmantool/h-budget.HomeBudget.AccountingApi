using System;

namespace HomeBudget.Components.Operations.Models
{
    public record UpdateTransferPayload
    {
        public Guid TransferOperationId { get; init; }
        public Guid Sender { get; init; }
        public Guid Recipient { get; init; }
        public decimal Amount { get; init; }
        public decimal Multiplier { get; init; }
        public DateOnly OperationAt { get; init; }
    }
}
