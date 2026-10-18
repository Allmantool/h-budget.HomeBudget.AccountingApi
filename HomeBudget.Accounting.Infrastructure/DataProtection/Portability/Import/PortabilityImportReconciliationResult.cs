namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportReconciliationResult(
        string State,
        string EvidenceSha256,
        string ErrorCode = null,
        string EntityType = null,
        string EntityId = null);
}
