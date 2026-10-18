using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Models
{
    internal sealed record ProjectionRebuildPlan(
        string Target,
        string MongoEndpoint,
        string MongoDatabase,
        string EventStoreEndpoint,
        DateTime CreatedUtc,
        IReadOnlyList<ProjectionRebuildPlanItem> Items)
    {
        public string EnvironmentId { get; init; } = "default";
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
        public bool ExternalSideEffectsSuppressed { get; init; }
        public string InventoryFingerprint { get; init; }
    }
}
