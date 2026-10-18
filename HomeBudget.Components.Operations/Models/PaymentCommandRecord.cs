using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentCommandRecord
    {
        public string CommandId { get; init; }

        public string LedgerId { get; init; }

        public string LedgerGenerationId { get; init; }

        public string GenerationToken { get; init; }

        public Guid PaymentAccountId { get; init; }

        public Guid PaymentOperationId { get; init; }

        public string CommandType { get; init; }

        public string RequestFingerprint { get; init; }

        public PaymentCommandStatus Status { get; init; }

        public DateTime AcceptedUtc { get; init; }

        public DateTime? PublishedUtc { get; init; }

        public DateTime? PersistedUtc { get; init; }

        public DateTime? ProjectedUtc { get; init; }
    }
}
