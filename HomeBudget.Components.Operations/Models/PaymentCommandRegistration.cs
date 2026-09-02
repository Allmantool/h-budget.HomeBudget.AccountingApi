using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentCommandRegistration
    {
        public string CommandId { get; init; }

        public Guid OperationId { get; init; }

        public bool WasAlreadyAccepted { get; init; }

        public string RequestFingerprint { get; init; }
    }
}
