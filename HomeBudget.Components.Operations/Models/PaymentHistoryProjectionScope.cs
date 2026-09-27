using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryProjectionScope(
        Guid PaymentAccountId,
        string FinancialPeriodIdentifier,
        string StreamId,
        long? PublishedRevision);
}
