using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal interface ICaptureCoordinationStore
    {
        Task<MaintenanceEpoch> CloseAdmissionAsync(
            CaptureRequest request,
            CancellationToken cancellationToken);

        Task<bool> IsAdmissionClosedAsync(
            string scopeKey,
            string generationId,
            CancellationToken cancellationToken);

        Task RecordReceiptAsync(
            MaintenanceEpoch epoch,
            CaptureComponentReceipt receipt,
            CancellationToken cancellationToken);

        Task RecordPendingWorkAsync(
            MaintenanceEpoch epoch,
            PendingWorkDisposition disposition,
            CancellationToken cancellationToken);

        Task StageManifestAsync(
            RecoveryManifestDraft draft,
            CancellationToken cancellationToken);

        Task UpdateStagedManifestAsync(
            RecoveryManifestDraft draft,
            CancellationToken cancellationToken);

        Task MarkManifestFailedAsync(
            Guid recoverySetId,
            string failureClassification,
            CancellationToken cancellationToken);

        Task PublishManifestAsync(
            Guid recoverySetId,
            IndependentCopyEvidence copy,
            IntegrityVerificationEvidence verification,
            CancellationToken cancellationToken);

        Task<DateTimeOffset?> GetProtectedCutoffAsync(
            string environmentId,
            string ledgerScope,
            CancellationToken cancellationToken);

        Task ReleaseEpochAsync(
            MaintenanceEpoch epoch,
            bool failed,
            CancellationToken cancellationToken);
    }
}
