namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record PendingWorkDisposition(
        string Disposition,
        long Count,
        long Bytes,
        long HighestAcceptanceSequence,
        string WatermarkJson);
}
