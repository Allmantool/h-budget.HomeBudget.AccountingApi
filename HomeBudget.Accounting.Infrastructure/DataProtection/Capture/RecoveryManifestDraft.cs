using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record RecoveryManifestDraft(
        Guid RecoverySetId,
        Guid OperationId,
        string EnvironmentId,
        string LedgerScope,
        string SourceGenerationId,
        DateTimeOffset CaptureStartedUtc,
        DateTimeOffset CommonCutoffUtc,
        string KeyReference,
        string ManifestChecksum,
        string ManifestJson,
        IReadOnlyCollection<CaptureComponentReceipt> Receipts,
        PendingWorkDisposition PendingWork);
}
