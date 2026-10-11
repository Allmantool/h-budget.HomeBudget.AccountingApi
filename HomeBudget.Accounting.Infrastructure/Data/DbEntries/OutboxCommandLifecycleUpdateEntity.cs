using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxCommandLifecycleUpdateEntity : IDbEntity
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
        public string MessageId { get; init; }
        public byte Status { get; init; }
        public byte DeadLetterStatus { get; init; }
        public byte PersistedStatus { get; init; }
        public string LastError { get; init; }
        public DateTime UpdatedUtc { get; init; }
    }
}
