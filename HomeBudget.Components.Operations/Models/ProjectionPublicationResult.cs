namespace HomeBudget.Components.Operations.Models
{
    public enum ProjectionPublicationState
    {
        Published,
        AlreadyPublished,
        Superseded
    }

    public sealed record ProjectionPublicationResult(
        ProjectionPublicationState State,
        string GenerationId,
        long CommittedRevision);
}
