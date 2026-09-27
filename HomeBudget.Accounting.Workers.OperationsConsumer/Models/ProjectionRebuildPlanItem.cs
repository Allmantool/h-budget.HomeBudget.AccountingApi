using System;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Models
{
    internal sealed record ProjectionRebuildPlanItem(
        Guid PaymentAccountId,
        string FinancialPeriodIdentifier,
        string StreamId,
        long ExpectedRevision,
        int ExpectedEventCount,
        long? PublishedRevision);
}
