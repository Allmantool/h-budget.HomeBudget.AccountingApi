using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection.Capture
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class SqlCaptureCoordinationStoreTests : BaseIntegrationTests
    {
        [Test]
        public async Task ReceiptsPersistAcrossOperationsAndRejectSequenceRegression()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var scopeKey = $"integration:capture:{Guid.NewGuid():N}";
            await RecordReceiptAsync(scopeKey, 20, cancellationToken);

            var regression = async () =>
                await RecordReceiptAsync(scopeKey, 19, cancellationToken);

            (await regression.Should().ThrowAsync<SqlException>())
                .Which.Message.Should().Contain("monotonic");
        }

        [Test]
        public async Task ManifestBecomesProtectedOnlyAfterIndependentImmutableVerifiedPublication()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var scopeKey = $"integration:capture:{Guid.NewGuid():N}";
            var context = await CreateContextAsync(scopeKey, cancellationToken);
            var cutoff = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
            var draft = CreateDraft(context.Operation.OperationId, cutoff);
            await context.CaptureStore.StageManifestAsync(draft, cancellationToken);

            var invalidPublication = async () => await context.CaptureStore.PublishManifestAsync(
                draft.RecoverySetId,
                new IndependentCopyEvidence("source-host-copy", false, false),
                new IntegrityVerificationEvidence(true, cutoff.AddMinutes(1)),
                cancellationToken);
            await invalidPublication.Should().ThrowAsync<InvalidOperationException>();
            (await context.CaptureStore.GetProtectedCutoffAsync(
                "integration",
                "default:legacy",
                cancellationToken)).Should().BeNull();

            await context.CaptureStore.PublishManifestAsync(
                draft.RecoverySetId,
                new IndependentCopyEvidence("independent-immutable-copy", true, true),
                new IntegrityVerificationEvidence(true, cutoff.AddMinutes(1)),
                cancellationToken);
            (await context.CaptureStore.GetProtectedCutoffAsync(
                "integration",
                "default:legacy",
                cancellationToken)).Should().Be(cutoff);

            await context.CaptureStore.ReleaseEpochAsync(context.Epoch, false, cancellationToken);
            await context.OperationStore.ReleaseLeaseAsync(context.Lease.CreateFenceContext(), cancellationToken);
        }

        [Test]
        public async Task AdmissionPermit_WhenEpochIsClosed_RejectsUntilExactEpochIsReleased()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var scopeKey = $"integration:capture:{Guid.NewGuid():N}";
            var context = await CreateContextAsync(scopeKey, cancellationToken);

            var blocked = async () =>
                await context.CaptureStore.AcquireAdmissionPermitAsync(
                    scopeKey,
                    "legacy",
                    cancellationToken);

            await blocked.Should().ThrowAsync<MaintenanceAdmissionClosedException>();

            var staleEpoch = context.Epoch with { EpochId = Guid.NewGuid() };
            var staleRelease = async () =>
                await context.CaptureStore.ReleaseEpochAsync(staleEpoch, false, cancellationToken);
            await staleRelease.Should().ThrowAsync<InvalidOperationException>();
            await blocked.Should().ThrowAsync<MaintenanceAdmissionClosedException>();

            await context.CaptureStore.ReleaseEpochAsync(context.Epoch, false, cancellationToken);
            await using (await context.CaptureStore.AcquireAdmissionPermitAsync(
                scopeKey,
                "legacy",
                cancellationToken))
            {
            }

            await context.OperationStore.ReleaseLeaseAsync(context.Lease.CreateFenceContext(), cancellationToken);
        }

        private async Task RecordReceiptAsync(
            string scopeKey,
            long sequence,
            CancellationToken cancellationToken)
        {
            var context = await CreateContextAsync(scopeKey, cancellationToken);
            try
            {
                await context.CaptureStore.RecordReceiptAsync(
                    context.Epoch,
                    new CaptureComponentReceipt(
                        "rates-sql",
                        "commit-frontier",
                        sequence,
                        new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
                        $"{{\"sequence\":{sequence}}}"),
                    cancellationToken);
            }
            finally
            {
                await context.CaptureStore.ReleaseEpochAsync(context.Epoch, false, CancellationToken.None);
                await context.OperationStore.ReleaseLeaseAsync(
                    context.Lease.CreateFenceContext(),
                    CancellationToken.None);
            }
        }

        private async Task<CaptureStoreContext> CreateContextAsync(
            string scopeKey,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var operationStore = new SqlDataProtectionOperationStore(TestContainers.AccountingDbConnectionString);
            var captureStore = new SqlCaptureCoordinationStore(TestContainers.AccountingDbConnectionString);
            var request = new DataProtectionOperationRequest
            {
                OperationId = Guid.NewGuid(),
                OperationKind = DataProtectionOperationKinds.Backup,
                ScopeKind = "ledger",
                ScopeKey = scopeKey,
                EnvironmentId = "integration",
                LedgerId = "default",
                GenerationId = "legacy",
                PlanFingerprint = new string('b', 64),
                ActorId = "capture-integration-test",
                CreatedUtc = now
            };
            var operation = await operationStore.EnqueueAsync(request, cancellationToken);
            var lease = await operationStore.TryAcquireLeaseAsync(
                operation.OperationId,
                scopeKey,
                "capture-integration-test",
                now,
                now.AddMinutes(5),
                cancellationToken);
            var epoch = await captureStore.CloseAdmissionAsync(
                new CaptureRequest
                {
                    Operation = operation,
                    FenceContext = lease.CreateFenceContext(),
                    TriggerReason = "integration-test",
                    KeyReference = "integration-key",
                    StartedUtc = new DateTimeOffset(now, TimeSpan.Zero),
                    RequiredComponents = new[] { "rates-sql" }
                },
                cancellationToken);
            return new CaptureStoreContext(operationStore, captureStore, operation, lease, epoch);
        }

        private static RecoveryManifestDraft CreateDraft(Guid operationId, DateTimeOffset cutoff) => new(
            Guid.NewGuid(),
            operationId,
            "integration",
            "default:legacy",
            "legacy",
            cutoff.AddMinutes(-1),
            cutoff,
            "integration-key",
            new string('c', 64),
            "{}",
            Array.Empty<CaptureComponentReceipt>(),
            new PendingWorkDisposition("drained", 0, 0, 0, "{}"));

        private sealed record CaptureStoreContext(
            SqlDataProtectionOperationStore OperationStore,
            SqlCaptureCoordinationStore CaptureStore,
            DataProtectionOperation Operation,
            DataProtectionLease Lease,
            MaintenanceEpoch Epoch);
    }
}
