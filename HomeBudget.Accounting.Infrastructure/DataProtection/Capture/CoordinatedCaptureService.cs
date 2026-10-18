using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection.Telemetry;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed class CoordinatedCaptureService
    {
        private readonly ICaptureCoordinationStore _store;
        private readonly IReadOnlyDictionary<string, ICaptureFrontierSource> _frontiers;
        private readonly IPendingWorkSource _pendingWork;
        private readonly IRecoveryArtifactPipeline _artifacts;
        private readonly CommonCutoffCalculator _cutoffCalculator;

        public CoordinatedCaptureService(
            ICaptureCoordinationStore store,
            IEnumerable<ICaptureFrontierSource> frontiers,
            IPendingWorkSource pendingWork,
            IRecoveryArtifactPipeline artifacts,
            CommonCutoffCalculator cutoffCalculator)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(frontiers);
            ArgumentNullException.ThrowIfNull(pendingWork);
            ArgumentNullException.ThrowIfNull(artifacts);
            ArgumentNullException.ThrowIfNull(cutoffCalculator);

            _store = store;
            _frontiers = frontiers.ToDictionary(frontier => frontier.ComponentName, StringComparer.Ordinal);
            _pendingWork = pendingWork;
            _artifacts = artifacts;
            _cutoffCalculator = cutoffCalculator;
        }

        public async Task<CaptureResult> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            var epoch = await _store.CloseAdmissionAsync(request, cancellationToken).ConfigureAwait(false);
            var failed = true;
            var failureStage = ProtectionFailureStage.Capture;

            try
            {
                await EnsureAdmissionClosedAsync(epoch, cancellationToken).ConfigureAwait(false);
                var pending = await ResolvePendingWorkAsync(request, epoch, cancellationToken).ConfigureAwait(false);
                await _store.RecordPendingWorkAsync(epoch, pending, cancellationToken).ConfigureAwait(false);
                DataProtectionTelemetry.SetAcceptedPendingWork(pending.Count);

                var receipts = await AcquireReceiptsAsync(request, epoch, cancellationToken).ConfigureAwait(false);
                foreach (var receipt in receipts)
                {
                    await _store.RecordReceiptAsync(epoch, receipt, cancellationToken).ConfigureAwait(false);
                }

                var cutoff = _cutoffCalculator.Calculate(receipts, request.RequiredComponents);
                var draft = RecoveryManifestFactory.CreateStaging(request, receipts, pending, cutoff);
                await _store.StageManifestAsync(draft, cancellationToken).ConfigureAwait(false);
                DataProtectionTelemetry.RecordRecoverySetCreated();

                try
                {
                    var captured = await _artifacts.CaptureAsync(draft, cancellationToken).ConfigureAwait(false);
                    if (captured.RecoverySetId != draft.RecoverySetId)
                    {
                        throw new CaptureStageException("capture.identity-mismatch");
                    }

                    draft = RecoveryManifestFactory.Complete(draft, captured.InventoryJson);
                    await _store.UpdateStagedManifestAsync(draft, cancellationToken).ConfigureAwait(false);
                    failureStage = ProtectionFailureStage.Copy;
                    var copy = await _artifacts.CopyAsync(draft, captured, cancellationToken).ConfigureAwait(false);
                    EnsureProtectedCopy(copy);
                    failureStage = ProtectionFailureStage.IntegrityVerification;
                    var verification = await _artifacts.VerifyAsync(
                        draft,
                        captured,
                        copy,
                        cancellationToken).ConfigureAwait(false);
                    EnsureVerified(verification);
                    DataProtectionTelemetry.RecordIntegrityVerified(DateTimeOffset.UtcNow);
                    failureStage = ProtectionFailureStage.Copy;
                    await _store.PublishManifestAsync(
                        draft.RecoverySetId,
                        copy,
                        verification,
                        cancellationToken).ConfigureAwait(false);
                    DataProtectionTelemetry.RecordIndependentlyProtected(cutoff);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    var classification = exception is CaptureStageException stageException
                        ? stageException.Classification
                        : "capture.unclassified";
                    await _store.MarkManifestFailedAsync(
                        draft.RecoverySetId,
                        classification,
                        cancellationToken).ConfigureAwait(false);
                    throw;
                }

                failed = false;
                return new CaptureResult(draft.RecoverySetId, cutoff, pending);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                DataProtectionTelemetry.RecordProtectionFailure(failureStage);
                throw;
            }
            finally
            {
                await _store.ReleaseEpochAsync(epoch, failed, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task EnsureAdmissionClosedAsync(
            MaintenanceEpoch epoch,
            CancellationToken cancellationToken)
        {
            var closed = await _store.IsAdmissionClosedAsync(
                epoch.ScopeKey,
                epoch.GenerationId,
                cancellationToken).ConfigureAwait(false);
            if (!closed)
            {
                throw new InvalidOperationException("Maintenance admission barrier is not closed.");
            }
        }

        private async Task<PendingWorkDisposition> ResolvePendingWorkAsync(
            CaptureRequest request,
            MaintenanceEpoch epoch,
            CancellationToken cancellationToken)
        {
            var snapshot = await _pendingWork.InspectAsync(epoch, cancellationToken).ConfigureAwait(false);
            if (snapshot.Count > 0 && request.PreferPendingDrain)
            {
                snapshot = await _pendingWork.DrainAsync(epoch, cancellationToken).ConfigureAwait(false);
            }

            if (snapshot.Count == 0)
            {
                return new PendingWorkDisposition(
                    "drained",
                    0,
                    0,
                    snapshot.HighestAcceptanceSequence,
                    snapshot.WatermarkJson);
            }

            if (!snapshot.RecoverySourceCapturable)
            {
                throw new InvalidOperationException(
                    "Accepted pending work neither settled nor has a capturable recovery source.");
            }

            return new PendingWorkDisposition(
                "included",
                snapshot.Count,
                snapshot.Bytes,
                snapshot.HighestAcceptanceSequence,
                snapshot.WatermarkJson);
        }

        private async Task<CaptureComponentReceipt[]> AcquireReceiptsAsync(
            CaptureRequest request,
            MaintenanceEpoch epoch,
            CancellationToken cancellationToken)
        {
            var sources = request.RequiredComponents.Select(component =>
            {
                if (!_frontiers.TryGetValue(component, out var source))
                {
                    throw new InvalidOperationException(
                        $"Required capture component '{component}' is not registered.");
                }

                return source;
            });
            var tasks = sources.Select(source => source.AcquireAsync(epoch, cancellationToken));
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static void EnsureProtectedCopy(IndependentCopyEvidence copy)
        {
            if (!copy.IsIndependent || !copy.IsImmutable)
            {
                throw new CaptureStageException("copy.unprotected");
            }
        }

        private static void EnsureVerified(IntegrityVerificationEvidence verification)
        {
            if (!verification.Succeeded)
            {
                throw new CaptureStageException("verification.failed");
            }
        }

        private static void ValidateRequest(CaptureRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Operation);
            ArgumentNullException.ThrowIfNull(request.FenceContext);
            ArgumentNullException.ThrowIfNull(request.RequiredComponents);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.TriggerReason);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyReference);
            if (request.Operation.OperationKind != DataProtectionOperationKinds.Backup)
            {
                throw new ArgumentException("Coordinated capture requires a backup operation.", nameof(request));
            }
        }
    }
}
