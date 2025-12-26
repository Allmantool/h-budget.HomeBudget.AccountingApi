using System;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxAccountPaymentsEntity : IDbEntity
    {
        public Guid Id { get; set; }

        public string AggregateId { get; set; }

        public string EventType { get; set; }

        public string Payload { get; set; }

        public string PartitionKey { get; set; }

        public DateTime CreatedAt { get; set; }

        public int RetryCount { get; set; }

        public byte Status { get; set; } = OutboxStatuses.Pending.Key;
    }
}