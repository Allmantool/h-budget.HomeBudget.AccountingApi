namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportRecordResult(
        string SourceEntityId,
        string Fingerprint,
        string Status,
        string TargetEntityId = null,
        string ErrorCode = null);
}
