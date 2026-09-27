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
        IReadOnlyList<ProjectionRebuildPlanItem> Items);
}
