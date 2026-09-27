using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryProjectionSnapshot(
        string FinancialPeriodIdentifier,
        Guid PaymentAccountId,
        string StreamId,
        long SourceRevision,
        string SourcePosition,
        string SnapshotHash,
        IReadOnlyList<PaymentOperationHistoryRecord> Records)
    {
        public decimal FinalBalance => Records.Count == 0 ? 0m : Records[^1].Balance;
    }
}
