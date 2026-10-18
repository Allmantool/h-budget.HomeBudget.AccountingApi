namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record FamilyProLegacyReference(
        string EntityType,
        string EntityId,
        string Reference);
}
