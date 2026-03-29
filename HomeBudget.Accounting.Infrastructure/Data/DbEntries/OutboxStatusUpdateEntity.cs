using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxStatusUpdateEntity : IDbEntity
    {
        public byte Status { get; init; }

        public DateTime UpdatedAt { get; init; }

        public string PartitionKey { get; init; }
    }
}
