using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record CaptureResult(
        Guid RecoverySetId,
        DateTimeOffset CommonCutoffUtc,
        PendingWorkDisposition PendingWork);
}
