using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.MongoDb
{
    [TestFixture]
    internal sealed class MongoDbProtectionProviderTests
    {
        private static readonly DateTimeOffset IntervalFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset IntervalTo = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset GapFrom = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset GapTo = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        private static readonly MongoDbRecoveryGap[] RecoveryGaps =
            [new MongoDbRecoveryGap(GapFrom, GapTo, "missing-oplog-segment")];
        private FakeMongoDbTopologyProbe _topology;
        private FakeMongoDbContinuousBackupAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            _topology = new FakeMongoDbTopologyProbe(ReplicaSetTopology());
            _adapter = new FakeMongoDbContinuousBackupAdapter(
                ReadyCapability(),
                ValidSnapshot(),
                ValidRecoveryPoint(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)));
        }

        [Test]
        public async Task ProbeCapabilityAsync_WithStandaloneTopology_ReportsBlockedWithoutCallingProvider()
        {
            _topology.Evidence = ReplicaSetTopology() with
            {
                Topology = MongoDbDeploymentTopology.Standalone,
                DeploymentId = "standalone",
                ReplicaSetName = null
            };

            var capability = await CreateProvider().ProbeCapabilityAsync(CancellationToken.None);

            capability.State.Should().Be(MongoDbPitrCapabilityState.Blocked);
            capability.ReasonCode.Should().Be("standalone-topology");
            _adapter.CapabilityProbeCount.Should().Be(0);
        }

        [Test]
        public async Task ProbeCapabilityAsync_WithUnsupportedBuild_ReportsBlockedWithoutCallingProvider()
        {
            _topology.Evidence = ReplicaSetTopology() with { ServerVersion = "7.0.42" };

            var capability = await CreateProvider().ProbeCapabilityAsync(CancellationToken.None);

            capability.State.Should().Be(MongoDbPitrCapabilityState.Blocked);
            capability.ReasonCode.Should().Be("unsupported-version");
            _adapter.CapabilityProbeCount.Should().Be(0);
        }

        [Test]
        public async Task ProbeCapabilityAsync_WithVerifiedContinuousProvider_ReportsActualIntervalAndGaps()
        {
            var capability = await CreateProvider().ProbeCapabilityAsync(CancellationToken.None);

            capability.State.Should().Be(MongoDbPitrCapabilityState.Ready);
            capability.ProviderId.Should().Be("backup-provider-a");
            capability.RecoverableFromUtc.Should().Be(IntervalFrom);
            capability.RecoverableToUtc.Should().Be(IntervalTo);
            capability.Gaps.Should().ContainSingle(gap => gap.ReasonCode == "missing-oplog-segment");
        }

        [TestCase(MongoDbProtectionMechanism.LogicalDump, "logical-dump-not-pitr")]
        [TestCase(MongoDbProtectionMechanism.FilesystemSnapshot, "continuous-history-unavailable")]
        public async Task ProbeCapabilityAsync_WithNonContinuousMechanism_ReportsBlocked(
            MongoDbProtectionMechanism mechanism,
            string reason)
        {
            _adapter.Capability = ReadyCapability() with { Mechanism = mechanism };

            var capability = await CreateProvider().ProbeCapabilityAsync(CancellationToken.None);

            capability.State.Should().Be(MongoDbPitrCapabilityState.Blocked);
            capability.ReasonCode.Should().Be(reason);
        }

        [Test]
        public async Task ProbeCapabilityAsync_WithoutImmutableArtifacts_ReportsBlocked()
        {
            _adapter.Capability = ReadyCapability() with { UsesImmutableArtifacts = false };

            var capability = await CreateProvider().ProbeCapabilityAsync(CancellationToken.None);

            capability.State.Should().Be(MongoDbPitrCapabilityState.Blocked);
            capability.ReasonCode.Should().Be("immutable-artifacts-unavailable");
        }

        [Test]
        public async Task ProbeCapabilityAsync_WhenSourceIdentityDrifts_ReportsBlocked()
        {
            _adapter.Capability = ReadyCapability() with { SourceDeploymentId = "other-deployment" };

            var capability = await CreateProvider().ProbeCapabilityAsync(CancellationToken.None);

            capability.State.Should().Be(MongoDbPitrCapabilityState.Blocked);
            capability.ReasonCode.Should().Be("source-identity-mismatch");
        }

        [Test]
        public async Task ProbeCapabilityAsync_WhenAdapterLeaksException_ReturnsSecretSafeFailure()
        {
            const string secret = "mongodb://user:password@live-host";
            _adapter.ProbeException = new InvalidOperationException(secret);

            var exception = await FluentActions.Awaiting(() =>
                    CreateProvider().ProbeCapabilityAsync(CancellationToken.None))
                .Should().ThrowAsync<MongoDbProtectionException>();
            exception.Which.Code.Should().Be("provider-probe-failed");
            exception.Which.Message.Should().NotContain(secret);
        }

        [Test]
        public async Task AcquireSnapshotAsync_WithValidEvidence_ReturnsImmutableSnapshotAndMutationInterval()
        {
            var snapshot = await CreateProvider().AcquireSnapshotAsync(CancellationToken.None);

            snapshot.SnapshotId.Should().Be("snapshot-20260901");
            snapshot.ProviderArtifactId.Should().Be("immutable/artifacts/snapshot-20260901");
            snapshot.SnapshotTimeUtc.Should().Be(IntervalFrom);
            snapshot.MutationFromUtc.Should().Be(IntervalFrom);
            snapshot.MutationToUtc.Should().Be(IntervalTo);
            snapshot.IsImmutable.Should().BeTrue();
            snapshot.IsVerified.Should().BeTrue();
        }

        [Test]
        public async Task AcquireSnapshotAsync_WhenEvidenceIdentityDrifts_FailsClosed()
        {
            _adapter.Snapshot = ValidSnapshot() with { SourceDeploymentId = "other-deployment" };

            await AssertFailureAsync("source-identity-mismatch", () =>
                CreateProvider().AcquireSnapshotAsync(CancellationToken.None));
        }

        [Test]
        public async Task AcquireSnapshotAsync_WhenArtifactIsMutable_FailsClosed()
        {
            _adapter.Snapshot = ValidSnapshot() with { IsImmutable = false };

            await AssertFailureAsync("snapshot-not-immutable", () =>
                CreateProvider().AcquireSnapshotAsync(CancellationToken.None));
        }

        [Test]
        public async Task PlanPointInTimeRecoveryAsync_WithCoveredUtcCutoff_ReturnsRequestedAndActualEvidence()
        {
            var requested = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

            var plan = await CreateProvider().PlanPointInTimeRecoveryAsync(requested, CancellationToken.None);

            plan.RequestedCutoffUtc.Should().Be(requested);
            plan.ActualCutoffUtc.Should().Be(requested.AddSeconds(-1));
            plan.ProviderArtifactId.Should().Be("immutable/artifacts/snapshot-20260901");
            plan.Precision.Should().Be("mongodb-timestamp");
        }

        [Test]
        public async Task PlanPointInTimeRecoveryAsync_WhenRequestedTimeFallsInGap_FailsWithoutCallingResolution()
        {
            await AssertFailureAsync("recovery-gap", () => CreateProvider().PlanPointInTimeRecoveryAsync(
                GapFrom.AddHours(1),
                CancellationToken.None));
        }

        [Test]
        public async Task PlanPointInTimeRecoveryAsync_WhenRequestedTimeIsOutsideInterval_FailsClosed()
        {
            await AssertFailureAsync("cutoff-unavailable", () => CreateProvider().PlanPointInTimeRecoveryAsync(
                IntervalFrom.AddSeconds(-1),
                CancellationToken.None));
        }

        [Test]
        public async Task PlanPointInTimeRecoveryAsync_WhenProviderChangesRequestedTime_FailsClosed()
        {
            var requested = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
            _adapter.RecoveryPoint = ValidRecoveryPoint(requested) with
            {
                RequestedCutoffUtc = requested.AddMinutes(-1)
            };

            await AssertFailureAsync("recovery-evidence-mismatch", () =>
                CreateProvider().PlanPointInTimeRecoveryAsync(requested, CancellationToken.None));
        }

        [Test]
        public async Task PlanPointInTimeRecoveryAsync_WhenActualTimeIsAfterRequest_FailsClosed()
        {
            var requested = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
            _adapter.RecoveryPoint = ValidRecoveryPoint(requested) with
            {
                ActualCutoffUtc = requested.AddSeconds(1)
            };

            await AssertFailureAsync("actual-cutoff-invalid", () =>
                CreateProvider().PlanPointInTimeRecoveryAsync(requested, CancellationToken.None));
        }

        [Test]
        public async Task PlanPointInTimeRecoveryAsync_WithNonUtcRequest_FailsClosed()
        {
            var requested = new DateTimeOffset(2026, 9, 20, 2, 0, 0, TimeSpan.FromHours(2));

            await AssertFailureAsync("cutoff-not-utc", () =>
                CreateProvider().PlanPointInTimeRecoveryAsync(requested, CancellationToken.None));
        }

        [Test]
        public async Task ProbeCapabilityAsync_WithCancellation_PropagatesCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await FluentActions.Awaiting(() => CreateProvider().ProbeCapabilityAsync(cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        private MongoDbProtectionProvider CreateProvider() => new(
            new MongoDbProtectionPolicy(
                "mongo-ledger-prod",
                new[] { "7.0.43" },
                new[] { "backup-provider-a" }),
            _topology,
            _adapter);

        private static MongoDbTopologyEvidence ReplicaSetTopology() => new(
            "7.0.43",
            MongoDbDeploymentTopology.ReplicaSet,
            "mongo-ledger-prod",
            "ledger-rs",
            DateTimeOffset.UtcNow);

        private static MongoDbProviderCapabilityEvidence ReadyCapability() => new(
            "backup-provider-a",
            "1.2.3",
            "mongo-ledger-prod",
            MongoDbProtectionMechanism.ContinuousBackup,
            true,
            true,
            IntervalFrom,
            IntervalTo,
            RecoveryGaps,
            DateTimeOffset.UtcNow);

        private static MongoDbSnapshotEvidence ValidSnapshot() => new(
            "backup-provider-a",
            "mongo-ledger-prod",
            "snapshot-20260901",
            "immutable/artifacts/snapshot-20260901",
            IntervalFrom,
            IntervalFrom,
            IntervalTo,
            IntervalFrom,
            IntervalTo,
            RecoveryGaps,
            true,
            true);

        private static MongoDbRecoveryPointEvidence ValidRecoveryPoint(DateTimeOffset requested) => new(
            "backup-provider-a",
            "mongo-ledger-prod",
            "snapshot-20260901",
            "immutable/artifacts/snapshot-20260901",
            requested,
            requested.AddSeconds(-1),
            IntervalFrom,
            IntervalFrom,
            IntervalTo,
            "mongodb-timestamp",
            true);

        private static async Task AssertFailureAsync(string code, Func<Task> action)
        {
            var exception = await FluentActions.Awaiting(action)
                .Should().ThrowAsync<MongoDbProtectionException>();
            exception.Which.Code.Should().Be(code);
        }
    }
}
