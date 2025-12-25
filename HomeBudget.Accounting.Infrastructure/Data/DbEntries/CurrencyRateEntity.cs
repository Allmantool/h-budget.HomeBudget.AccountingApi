using System;

using HomeBudget.Core.Constants;

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

        public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
        public int RetryCount { get; set; }
    }
}