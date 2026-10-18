namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed record PortabilityImportReconciliationComparison(
        bool Succeeded,
        string EvidenceSha256,
        string ErrorCode = null,
        string EntityType = null,
        string EntityId = null);
}
