using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

using NUnit.Framework;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection.Capture
{
    [TestFixture]
    public sealed class CoordinatedCaptureServiceTests
    {
        [Test]
        public async Task CaptureAsync_ClosesAdmissionBeforeConcurrentFrontiersAndUsesEarliestCutoff()
        {
            var fixture = new CaptureFixture();
            fixture.AddFrontier("accounting-sql", 42, Utc(10), attemptMutation: true);
            fixture.AddFrontier("mongo-reference", 18, Utc(12), attemptMutation: true);
            fixture.AddFrontier("rates-sql", 73, Utc(11), attemptMutation: true);
            fixture.AddFrontier("eventstore", 41, Utc(13), attemptMutation: true);

            var result = await fixture.CreateService().CaptureAsync(fixture.Request, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.CommonCutoffUtc, Is.EqualTo(Utc(10)));
                Assert.That(fixture.Store.AdmissionWasClosedBeforeEveryReceipt, Is.True);
                Assert.That(
                    fixture.Frontiers.Where(frontier => frontier.AttemptedMutation),
                    Has.All.Matches<ControlledFrontier>(frontier => frontier.MutationWasRejected));
                Assert.That(fixture.Store.PublishedRecoverySetId, Is.EqualTo(result.RecoverySetId));
            });
        }

        [Test]
        public async Task CaptureAsync_RejectsARegressingDurableComponentReceipt()
        {
            var fixture = new CaptureFixture();
            fixture.Store.SeedSequence("rates-sql", "commit-frontier", 90);
            fixture.AddFrontier("rates-sql", 89, Utc(10));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CreateService().CaptureAsync(fixture.Request, CancellationToken.None));

            Assert.That(exception!.Message, Does.Contain("monotonic"));
            Assert.That(fixture.Store.PublishedRecoverySetId, Is.Null);
        }

        [Test]
        public async Task CaptureAsync_IncludesAcceptedUnpersistedWorkWhenBoundedDrainCannotSettleIt()
        {
            var fixture = new CaptureFixture();
            fixture.AddFrontier("accounting-sql", 102, Utc(10));
            fixture.Pending.Initial = new PendingWorkSnapshot(3, 512, 101, true, "{\"outboxSequence\":101}");
            fixture.Pending.AfterDrain = fixture.Pending.Initial;

            var result = await fixture.CreateService().CaptureAsync(fixture.Request, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.PendingWork.Disposition, Is.EqualTo("included"));
                Assert.That(result.PendingWork.Count, Is.EqualTo(3));
                Assert.That(result.PendingWork.HighestAcceptanceSequence, Is.EqualTo(101));
                Assert.That(fixture.Store.RecordedPendingWork, Is.EqualTo(result.PendingWork));
            });
        }

        [TestCase(FailureStage.Capture, "capture.failed")]
        [TestCase(FailureStage.Copy, "copy.failed")]
        [TestCase(FailureStage.Verification, "verification.failed")]
        public async Task CaptureAsync_FailureNeverAdvancesProtectedCutoff(
            FailureStage failureStage,
            string expectedClassification)
        {
            var fixture = new CaptureFixture
            {
                Pipeline = new ControlledArtifactPipeline(failureStage)
            };
            fixture.Store.ProtectedCutoff = Utc(5);
            fixture.AddFrontier("accounting-sql", 42, Utc(10));

            await Assert.ThrowsAsync<CaptureStageException>(() =>
                fixture.CreateService().CaptureAsync(fixture.Request, CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Store.ProtectedCutoff, Is.EqualTo(Utc(5)));
                Assert.That(fixture.Store.PublishedRecoverySetId, Is.Null);
                Assert.That(fixture.Store.FailureClassification, Is.EqualTo(expectedClassification));
                Assert.That(fixture.Store.EpochReleased, Is.True);
            });
        }

        private static DateTimeOffset Utc(int minute) =>
            new(2026, 10, 5, 12, minute, 0, TimeSpan.Zero);

        private sealed class CaptureFixture
        {
            private readonly List<ControlledFrontier> _frontiers = [];
            private readonly List<string> _requiredComponents = [];

            public CaptureFixture()
            {
                var operationId = Guid.NewGuid();
                Request = new CaptureRequest
                {
                    Operation = new DataProtectionOperation
                    {
                        OperationId = operationId,
                        OperationKind = DataProtectionOperationKinds.Backup,
                        ScopeKind = "ledger",
                        ScopeKey = "integration:ledger:default:legacy",
                        EnvironmentId = "integration",
                        LedgerId = "default",
                        GenerationId = "legacy",
                        PlanFingerprint = new string('a', 64),
                        StateValue = "running",
                        CreatedUtc = Utc(0).UtcDateTime,
                        UpdatedUtc = Utc(0).UtcDateTime
                    },
                    FenceContext = new DataProtectionOperationFenceContext
                    {
                        OperationId = operationId,
                        LeaseId = Guid.NewGuid(),
                        ScopeKey = "integration:ledger:default:legacy",
                        OwnerId = "capture-test",
                        FenceToken = 1
                    },
                    TriggerReason = "on-demand:test",
                    KeyReference = "test-key-reference",
                    StartedUtc = Utc(1),
                    RequiredComponents = _requiredComponents
                };
            }

            public CaptureRequest Request { get; }

            public ControlledCaptureStore Store { get; } = new();

            public ControlledPendingWorkSource Pending { get; } = new();

            public ControlledArtifactPipeline Pipeline { get; set; } = new(FailureStage.None);

            public IReadOnlyCollection<ControlledFrontier> Frontiers => _frontiers;

            public void AddFrontier(
                string component,
                long sequence,
                DateTimeOffset committedUtc,
                bool attemptMutation = false)
            {
                _frontiers.Add(new ControlledFrontier(Store, component, sequence, committedUtc, attemptMutation));
                _requiredComponents.Add(component);
            }

            public CoordinatedCaptureService CreateService() =>
                new(Store, _frontiers, Pending, Pipeline, new CommonCutoffCalculator());
        }

        private sealed class ControlledFrontier(
            ControlledCaptureStore store,
            string componentName,
            long sequence,
            DateTimeOffset committedUtc,
            bool attemptMutation) : ICaptureFrontierSource
        {
            public string ComponentName { get; } = componentName;

            public bool AttemptedMutation { get; private set; }

            public bool MutationWasRejected { get; private set; }

            public Task<CaptureComponentReceipt> AcquireAsync(
                MaintenanceEpoch epoch,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attemptMutation)
                {
                    AttemptedMutation = true;
                    MutationWasRejected = store.AdmissionClosed;
                }

                store.ReceiptObservedAdmissionStates.Add(store.AdmissionClosed);
                return Task.FromResult(new CaptureComponentReceipt(
                    ComponentName,
                    "commit-frontier",
                    sequence,
                    committedUtc,
                    $"{{\"sequence\":{sequence}}}"));
            }
        }

        private sealed class ControlledPendingWorkSource : IPendingWorkSource
        {
            public PendingWorkSnapshot Initial { get; set; } = new(0, 0, 0, true, "{}");

            public PendingWorkSnapshot AfterDrain { get; set; } = new(0, 0, 0, true, "{}");

            public Task<PendingWorkSnapshot> InspectAsync(
                MaintenanceEpoch epoch,
                CancellationToken cancellationToken) => Task.FromResult(Initial);

            public Task<PendingWorkSnapshot> DrainAsync(
                MaintenanceEpoch epoch,
                CancellationToken cancellationToken) => Task.FromResult(AfterDrain);
        }

        public enum FailureStage
        {
            None,
            Capture,
            Copy,
            Verification
        }

        private sealed class ControlledArtifactPipeline(FailureStage failureStage) : IRecoveryArtifactPipeline
        {
            public Task<CapturedArtifactEvidence> CaptureAsync(
                RecoveryManifestDraft draft,
                CancellationToken cancellationToken)
            {
                ThrowIf(FailureStage.Capture, "capture.failed");
                return Task.FromResult(new CapturedArtifactEvidence(draft.RecoverySetId, "[]"));
            }

            public Task<IndependentCopyEvidence> CopyAsync(
                RecoveryManifestDraft draft,
                CapturedArtifactEvidence capture,
                CancellationToken cancellationToken)
            {
                ThrowIf(FailureStage.Copy, "copy.failed");
                return Task.FromResult(new IndependentCopyEvidence("independent-copy", true, true));
            }

            public Task<IntegrityVerificationEvidence> VerifyAsync(
                RecoveryManifestDraft draft,
                CapturedArtifactEvidence capture,
                IndependentCopyEvidence copy,
                CancellationToken cancellationToken)
            {
                ThrowIf(FailureStage.Verification, "verification.failed");
                return Task.FromResult(new IntegrityVerificationEvidence(true, Utc(15)));
            }

            private void ThrowIf(FailureStage stage, string classification)
            {
                if (failureStage == stage)
                {
                    throw new CaptureStageException(classification);
                }
            }
        }

        private sealed class ControlledCaptureStore : ICaptureCoordinationStore
        {
            private readonly Dictionary<(string Component, string Kind), long> _sequences = new();

            public bool AdmissionClosed { get; private set; }

            public List<bool> ReceiptObservedAdmissionStates { get; } = [];

            public bool AdmissionWasClosedBeforeEveryReceipt =>
                ReceiptObservedAdmissionStates.Count > 0 && ReceiptObservedAdmissionStates.All(closed => closed);

            public PendingWorkDisposition RecordedPendingWork { get; private set; }

            public DateTimeOffset? ProtectedCutoff { get; set; }

            public Guid? PublishedRecoverySetId { get; private set; }

            public string FailureClassification { get; private set; }

            public bool EpochReleased { get; private set; }

            public RecoveryManifestDraft Draft { get; private set; }

            public void SeedSequence(string component, string kind, long sequence) =>
                _sequences[(component, kind)] = sequence;

            public Task<MaintenanceEpoch> CloseAdmissionAsync(
                CaptureRequest request,
                CancellationToken cancellationToken)
            {
                AdmissionClosed = true;
                return Task.FromResult(new MaintenanceEpoch(
                    Guid.NewGuid(),
                    request.Operation.OperationId,
                    request.Operation.ScopeKey,
                    request.Operation.GenerationId,
                    1,
                    request.StartedUtc));
            }

            public Task<bool> IsAdmissionClosedAsync(
                string scopeKey,
                string generationId,
                CancellationToken cancellationToken) => Task.FromResult(AdmissionClosed);

            public Task RecordReceiptAsync(
                MaintenanceEpoch epoch,
                CaptureComponentReceipt receipt,
                CancellationToken cancellationToken)
            {
                var key = (receipt.ComponentName, receipt.ReceiptKind);
                if (_sequences.TryGetValue(key, out var previous) && receipt.ProtectionSequence < previous)
                {
                    throw new InvalidOperationException("Component receipt sequence must be monotonic.");
                }

                _sequences[key] = receipt.ProtectionSequence;
                return Task.CompletedTask;
            }

            public Task RecordPendingWorkAsync(
                MaintenanceEpoch epoch,
                PendingWorkDisposition disposition,
                CancellationToken cancellationToken)
            {
                RecordedPendingWork = disposition;
                return Task.CompletedTask;
            }

            public Task StageManifestAsync(RecoveryManifestDraft draft, CancellationToken cancellationToken)
            {
                Draft = draft;
                return Task.CompletedTask;
            }

            public Task UpdateStagedManifestAsync(
                RecoveryManifestDraft draft,
                CancellationToken cancellationToken)
            {
                Draft = draft;
                return Task.CompletedTask;
            }

            public Task MarkManifestFailedAsync(
                Guid recoverySetId,
                string failureClassification,
                CancellationToken cancellationToken)
            {
                FailureClassification = failureClassification;
                return Task.CompletedTask;
            }

            public Task PublishManifestAsync(
                Guid recoverySetId,
                IndependentCopyEvidence copy,
                IntegrityVerificationEvidence verification,
                CancellationToken cancellationToken)
            {
                if (!copy.IsIndependent || !copy.IsImmutable || !verification.Succeeded)
                {
                    throw new InvalidOperationException("Manifest publication requires protected verification evidence.");
                }

                PublishedRecoverySetId = recoverySetId;
                ProtectedCutoff = Draft.CommonCutoffUtc;
                return Task.CompletedTask;
            }

            public Task<DateTimeOffset?> GetProtectedCutoffAsync(
                string environmentId,
                string ledgerScope,
                CancellationToken cancellationToken) => Task.FromResult(ProtectedCutoff);

            public Task ReleaseEpochAsync(
                MaintenanceEpoch epoch,
                bool failed,
                CancellationToken cancellationToken)
            {
                AdmissionClosed = false;
                EpochReleased = true;
                return Task.CompletedTask;
            }
        }
    }
}
