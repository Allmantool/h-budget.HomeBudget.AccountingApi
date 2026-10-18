namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed record SqlServerCaptureResult(
        SqlServerCapturePlan Plan,
        SqlServerBackupMetadata Metadata);
}
