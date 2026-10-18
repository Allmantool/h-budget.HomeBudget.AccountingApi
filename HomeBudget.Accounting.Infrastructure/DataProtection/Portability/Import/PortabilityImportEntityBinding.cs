namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportEntityBinding(
        string EntityType,
        string SourceEntityId,
        string Fingerprint,
        string IdempotencyKey,
        string EntryPath);
}
