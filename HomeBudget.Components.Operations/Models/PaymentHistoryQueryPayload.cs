using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryQueryPayload
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; }
        public string SortBy { get; init; }
        public string SortDirection { get; init; }
        public DateOnly? DateFrom { get; init; }
        public DateOnly? DateTo { get; init; }
        public string Type { get; init; }
        public string CategoryId { get; init; }
        public string ContractorId { get; init; }
        public decimal? AmountMin { get; init; }
        public decimal? AmountMax { get; init; }
    }
}
