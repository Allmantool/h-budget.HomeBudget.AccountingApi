using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxStatusUpdateEntity : IDbEntity
    {
        public byte Status { get; init; }

        public byte PersistedStatus { get; init; }

        public byte ProjectedStatus { get; init; }

        public byte DeadLetterStatus { get; init; }

        public DateTime UpdatedAt { get; init; }

        public string MessageId { get; init; }
    }
}
