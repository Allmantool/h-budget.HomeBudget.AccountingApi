using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record CaptureComponentReceipt(
        string ComponentName,
        string ReceiptKind,
        long ProtectionSequence,
        DateTimeOffset CommittedUtc,
        string WatermarkJson);
}
