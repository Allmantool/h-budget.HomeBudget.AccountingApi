using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxLockParameters : IDbEntity
    {
        public byte PendingStatus { get; init; }

        public byte FailedStatus { get; init; }

        public int MaxRetryAttempts { get; init; }

        public int BatchSize { get; init; }

        public string LockedBy { get; init; }

        public DateTime NowUtc { get; init; }

        public DateTime LockedUntilUtc { get; init; }
    }
}
