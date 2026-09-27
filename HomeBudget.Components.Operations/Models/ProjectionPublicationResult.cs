namespace HomeBudget.Components.Operations.Models
{
    public sealed record ProjectionPublicationResult(
        ProjectionPublicationState State,
        string GenerationId,
        long CommittedRevision);
}
