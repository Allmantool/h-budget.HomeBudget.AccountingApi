using System;
using System.Collections.Generic;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryQuery
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 25;
        public PaymentHistorySortField SortBy { get; init; } = PaymentHistorySortField.Date;
        public PaymentHistorySortDirection SortDirection { get; init; } = PaymentHistorySortDirection.Desc;
        public DateOnly? DateFrom { get; init; }
        public DateOnly? DateTo { get; init; }
        public IReadOnlyCollection<Guid> CategoryIds { get; init; } = Array.Empty<Guid>();
        public bool HasCategoryFilter { get; init; }
        public Guid? ContractorId { get; init; }
        public decimal? AmountMin { get; init; }
        public decimal? AmountMax { get; init; }
    }
}
