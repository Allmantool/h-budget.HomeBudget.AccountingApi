using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed class ProjectionAuditRecord
    {
        public string StreamId { get; init; }
        public string Revision { get; init; }
        public string Position { get; init; }
        public Guid ProjectionRunId { get; init; }
        public string Status { get; init; }
        public string Error { get; init; }
        public DateTime StartedUtc { get; init; }
        public DateTime UpdatedUtc { get; init; }
        public DateTime? CompletedUtc { get; init; }
    }
}

