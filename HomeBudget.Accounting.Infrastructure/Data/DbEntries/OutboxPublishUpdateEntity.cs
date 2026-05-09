using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxPublishUpdateEntity : IDbEntity
    {
        public string MessageId { get; init; }

        public string LockedBy { get; init; }

        public byte PublishedStatus { get; init; }

        public DateTime PublishedUtc { get; init; }
    }
}
