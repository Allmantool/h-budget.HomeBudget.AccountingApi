namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record FamilyProLineageIdentity(
        string LineageId,
        string EntityType,
        string EntityId,
        string SourceEntityId,
        string SourceRunId,
        string SourceBatchId,
        string MappingId);
}
