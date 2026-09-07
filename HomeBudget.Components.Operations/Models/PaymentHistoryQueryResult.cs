using System.Collections.Generic;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryQueryResult(
        IReadOnlyCollection<PaymentHistoryDocument> Items,
        long TotalCount);
}
