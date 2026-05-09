using System;

namespace HomeBudget.Accounting.Infrastructure.Data.DbEntries
{
    public sealed record PaymentInboxMessageUpdateEntity : IDbEntity
    {
        public string MessageId { get; init; }

        public string Status { get; init; }

        public string LastError { get; init; }

        public int MaxRetryAttempts { get; init; }

        public DateTime UpdatedUtc { get; init; }
    }
}
