using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxFailureUpdateEntity : IDbEntity
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
        public string MessageId { get; init; }

        public string LockedBy { get; init; }

        public string LastError { get; init; }

        public int MaxRetryAttempts { get; init; }

        public byte FailedStatus { get; init; }

        public byte DeadLetterStatus { get; init; }

        public byte PersistedStatus { get; init; }

        public DateTime UpdatedUtc { get; init; }
    }
}
