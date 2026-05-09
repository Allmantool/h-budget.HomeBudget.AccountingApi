using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record PaymentInboxMessageEntity : IDbEntity
    {
        public string MessageId { get; init; }

        public string Topic { get; init; }

        public int Partition { get; init; }

        public long Offset { get; init; }

        public string Status { get; init; }

        public int RetryCount { get; init; }

        public string LastError { get; init; }

        public DateTime CreatedUtc { get; init; }

        public DateTime UpdatedUtc { get; init; }

        public DateTime? ProcessedUtc { get; init; }

        public string RawMessage { get; init; }
    }
}
