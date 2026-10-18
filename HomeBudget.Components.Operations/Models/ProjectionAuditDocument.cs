using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Operations.Models
{
    public sealed class ProjectionAuditDocument : DocumentEntity<ProjectionAuditRecord>
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
    }
}

