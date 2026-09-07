using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.History
{
    public sealed record PaymentHistoryPageResponse
    {
        public IReadOnlyCollection<PaymentOperationHistoryRecordResponse> Items { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
        public long TotalCount { get; init; }
        public int TotalPages { get; init; }
        public bool HasPreviousPage { get; init; }
        public bool HasNextPage { get; init; }
    }
}
