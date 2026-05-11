namespace HomeBudget.Components.Operations.Models
{
    public sealed class ProjectionCheckpoint
    {
        public string StreamId { get; init; }
        public string Revision { get; init; }
        public string Position { get; init; }
    }
}

