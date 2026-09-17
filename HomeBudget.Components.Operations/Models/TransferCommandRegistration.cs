using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record TransferCommandRegistration
    {
        public Guid TransferId { get; init; }
        public string CommandId { get; init; }
        public string RequestFingerprint { get; init; }
        public string SenderCommandId { get; init; }
        public string RecipientCommandId { get; init; }
        public bool WasAlreadyAccepted { get; init; }
    }
}
