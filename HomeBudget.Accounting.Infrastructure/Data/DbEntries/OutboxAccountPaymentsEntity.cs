using System;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record OutboxAccountPaymentsEntity : IDbEntity
    {
        public Guid Id { get; init; }

        public string AggregateId { get; init; }

        public string OperationId { get; init; }

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

        public DateTime CreatedUtc { get; init; }

        public DateTime UpdatedUtc { get; init; }

        public DateTime? PublishedAt { get; set; }

        public DateTime? PublishedUtc { get; set; }

        public DateTime? ProcessedAt { get; set; }

        public string LockedBy { get; init; }

        public DateTime? LockedUntilUtc { get; init; }

        public int RetryCount { get; set; }

        public byte Status { get; set; } = OutboxStatus.Pending.Key;

        public string LastError { get; set; }

        public void MarkPublished()
        {
            Status = OutboxStatus.Published.Key;
            PublishedAt = DateTime.UtcNow;
            PublishedUtc = PublishedAt;
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
