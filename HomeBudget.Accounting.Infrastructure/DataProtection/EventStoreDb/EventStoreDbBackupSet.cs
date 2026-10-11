using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbBackupSet(
        Guid CaptureId,
        EventStoreDbCaptureKind Kind,
        string SourceNodeId,
        string ServerVersion,
        long WriterPosition,
        long ChaserPosition,
        string ArtifactRoot,
        Guid? ParentCaptureId,
        bool IsIntegrityVerified,
        IReadOnlyCollection<EventStoreDbBackupFile> Inventory);
}
