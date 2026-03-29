using System;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxAccountPaymentsEntity : IDbEntity
    {
        public Guid Id { get; init; }

        public string AggregateId { get; init; }

        public string EventType { get; init; }

        public string Payload { get; init; }

        public string PartitionKey { get; init; }

        public string CorrelationId { get; init; }

        public string MessageId { get; init; }

        public string CausationId { get; init; }

        public string TraceParent { get; init; }

        public string TraceState { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public DateTime? PublishedAt { get; private set; }

        public DateTime? ProcessedAt { get; private set; }

        public int RetryCount { get; private set; }

        public byte Status { get; private set; } = OutboxStatus.Pending.Key;

        public string LastError { get; private set; }

        public void MarkPublished()
        {
            Status = OutboxStatus.Published.Key;
            PublishedAt = DateTime.UtcNow;
        }

        public void MarkAcknowledged()
        {
            Status = OutboxStatus.Acknowledged.Key;
            ProcessedAt = DateTime.UtcNow;
        }

        public void MarkRetry(string error)
        {
            RetryCount++;
            Status = OutboxStatus.Retrying.Key;
            LastError = error;
        }

        public void MarkFailed(string error)
        {
            Status = OutboxStatus.Failed.Key;
            LastError = error;
        }

        public void MarkDeadLettered(string error)
        {
            Status = OutboxStatus.DeadLettered.Key;
            LastError = error;
        }
    }
}
