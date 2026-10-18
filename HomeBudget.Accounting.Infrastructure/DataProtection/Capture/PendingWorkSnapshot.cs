namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record PendingWorkSnapshot(
        long Count,
        long Bytes,
        long HighestAcceptanceSequence,
        bool RecoverySourceCapturable,
        string WatermarkJson);
}
