using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryProjectionHead
    {
        public string FinancialPeriodIdentifier { get; init; }
        public Guid PaymentAccountId { get; init; }
        public string StreamId { get; init; }
        public long SourceRevision { get; init; }
        public string SourcePosition { get; init; }
        public string SnapshotHash { get; init; }
        public string GenerationId { get; init; }
        public int RecordCount { get; init; }
        public decimal FinalBalance { get; init; }
        public DateTime PublishedUtc { get; init; }
    }
}
