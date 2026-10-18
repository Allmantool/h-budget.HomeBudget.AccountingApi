using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbRestorePlan(
        Guid PlanId,
        Guid CaptureId,
        string SourceNodeId,
        string TargetId,
        long WriterPosition,
        long ChaserPosition,
        DateTimeOffset PlannedAtUtc,
        IReadOnlyCollection<EventStoreDbCopyStep> Steps,
        string TruncateCheckpointSourceRelativePath,
        string TruncateCheckpointDestinationRelativePath);
}
