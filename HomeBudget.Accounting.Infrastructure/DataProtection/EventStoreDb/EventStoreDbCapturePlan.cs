using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbCapturePlan(
        Guid PlanId,
        EventStoreDbCaptureKind Kind,
        string SourceNodeId,
        string ServerVersion,
        long WriterPosition,
        long ChaserPosition,
        DateTimeOffset PlannedAtUtc,
        Guid? ParentCaptureId,
        IReadOnlyCollection<EventStoreDbBackupFile> Inventory,
        IReadOnlyCollection<EventStoreDbCopyStep> Steps);
}
