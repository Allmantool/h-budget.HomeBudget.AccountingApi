using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.EventStoreDb
{
    [TestFixture]
    internal sealed class EventStoreDbProtectionProviderTests
    {
        private DisposableEventStoreDbProtectionRoot _root;

        [SetUp]
        public void SetUp() => _root = new DisposableEventStoreDbProtectionRoot();

        [TearDown]
        public void TearDown() => _root.Dispose();

        [Test]
        public async Task PlanFullCaptureAsync_WithCompatibleSource_ProducesOrderedChecksummedInventory()
        {
            var plan = await _root.CreateProvider().PlanCaptureAsync(
                EventStoreDbCaptureKind.FullFileCopy,
                Guid.NewGuid(),
                null,
                CancellationToken.None);

            plan.SourceNodeId.Should().Be("source-node-a");
            plan.ServerVersion.Should().Be("24.10.16");
            plan.ChaserPosition.Should().Be(3072);
            plan.WriterPosition.Should().Be(4096);
            plan.Inventory.Should().Contain(file => file.RelativePath == "db/writer.chk");
            plan.Inventory.Should().Contain(file => file.RelativePath == "index/indexmap");
            plan.Inventory.Should().Contain(file => file.Role == EventStoreDbFileRole.Chunk);
            plan.Inventory.Should().OnlyContain(file => file.Sha256.Length == 64 && file.SizeBytes > 0);
            plan.Inventory.First(file => file.Role == EventStoreDbFileRole.Chunk)
                .Dependencies.Should().BeEquivalentTo("db/chaser.chk", "db/writer.chk");
            plan.Inventory.Single(file => file.Role == EventStoreDbFileRole.IndexTable)
                .Dependencies.Should().Contain("index/indexmap");
            plan.Steps.Select(step => step.Order).Should().BeInAscendingOrder();
            plan.Steps.Select(step => step.DestinationRelativePath).Should().OnlyHaveUniqueItems();
            plan.Steps.Should().OnlyContain(step =>
                step.Action == EventStoreDbCopyAction.Copy && Path.IsPathFullyQualified(step.SourcePath));
        }

        [TestCase("24.10.15", "unsupported-version")]
        [TestCase("25.1.0", "unsupported-version")]
        public async Task PlanCaptureAsync_WithUnsupportedVersion_FailsClosed(string version, string code)
        {
            _root.Probe.Source = _root.Probe.Source with { ServerVersion = version };

            await AssertFailureAsync(code, () => PlanFullAsync());
        }

        [Test]
        public async Task PlanCaptureAsync_WhenNodeIdentityDrifts_FailsWithoutDisclosingPath()
        {
            const string secret = "credential-like-segment";
            _root.Probe.Source = _root.Probe.Source with
            {
                Identity = new EventStoreDbNodePathIdentity(
                    "source-node-b",
                    Path.Combine(_root.Root, secret, "db"),
                    _root.IndexPath)
            };

            var exception = await FluentActions.Awaiting(PlanFullAsync)
                .Should().ThrowAsync<EventStoreDbProtectionException>();
            exception.Which.Code.Should().Be("source-identity-mismatch");
            exception.Which.Message.Should().NotContain(secret);
            exception.Which.Message.Should().NotContain(_root.Root);
        }

        [Test]
        public async Task PlanCaptureAsync_WhenScavengeIsActive_FailsClosed()
        {
            _root.Probe.Source = _root.Probe.Source with { IsScavengeRunning = true };

            await AssertFailureAsync("scavenge-active", () => PlanFullAsync());
        }

        [Test]
        public async Task PlanCaptureAsync_WhenCheckpointSetIsIncomplete_FailsClosed()
        {
            File.Delete(Path.Combine(_root.DatabasePath, "writer.chk"));

            await AssertFailureAsync("database-inventory-incomplete", () => PlanFullAsync());
        }

        [Test]
        public async Task PlanCaptureAsync_WhenIndexSetIsIncomplete_FailsClosed()
        {
            File.Delete(Path.Combine(_root.IndexPath, "indexmap"));

            await AssertFailureAsync("index-inventory-incomplete", () => PlanFullAsync());
        }

        [Test]
        public async Task PlanCaptureAsync_WhenChaserExceedsWriter_FailsClosed()
        {
            _root.Probe.Source = _root.Probe.Source with { ChaserPosition = 4097 };

            await AssertFailureAsync("checkpoint-position-invalid", () => PlanFullAsync());
        }

        [Test]
        public async Task PlanDifferentialCaptureAsync_WithVerifiedParent_CopiesOnlyChangedFiles()
        {
            var fullPlan = await PlanFullAsync();
            var parent = _root.MaterializeBackup(fullPlan);
            await File.AppendAllTextAsync(
                Path.Combine(_root.DatabasePath, "chunk-000001.000000"),
                "-changed");

            var differential = await _root.CreateProvider().PlanCaptureAsync(
                EventStoreDbCaptureKind.DifferentialFileCopy,
                Guid.NewGuid(),
                parent,
                CancellationToken.None);

            differential.ParentCaptureId.Should().Be(parent.CaptureId);
            differential.Inventory.Should().HaveSameCount(fullPlan.Inventory);
            differential.Steps.Should().ContainSingle(
                step => step.DestinationRelativePath == "db/chunk-000001.000000");
            differential.Steps.Count.Should().BeLessThan(fullPlan.Steps.Count);
        }

        [Test]
        public async Task PlanDifferentialCaptureAsync_WithMixedNodeParent_FailsClosed()
        {
            var fullPlan = await PlanFullAsync();
            var parent = _root.MaterializeBackup(fullPlan) with { SourceNodeId = "source-node-b" };

            await AssertFailureAsync(
                "backup-node-mismatch",
                () => _root.CreateProvider().PlanCaptureAsync(
                    EventStoreDbCaptureKind.DifferentialFileCopy,
                    Guid.NewGuid(),
                    parent,
                    CancellationToken.None));
        }

        [Test]
        public async Task PlanRestoreAsync_WithStoppedDedicatedTarget_ProducesTruncateFromChaserPlan()
        {
            var capture = _root.MaterializeBackup(await PlanFullAsync());

            var plan = await _root.CreateProvider().PlanRestoreAsync(
                Guid.NewGuid(),
                capture,
                _root.TargetIdentity.NodeId,
                CancellationToken.None);

            plan.SourceNodeId.Should().Be(_root.SourceIdentity.NodeId);
            plan.TargetId.Should().Be(_root.TargetIdentity.NodeId);
            plan.TruncateCheckpointSourceRelativePath.Should().Be("db/chaser.chk");
            plan.TruncateCheckpointDestinationRelativePath.Should().Be("db/truncate.chk");
            plan.Steps.Should().HaveSameCount(capture.Inventory);
        }

        [Test]
        public async Task PlanRestoreAsync_WhenTargetIsRunning_FailsClosed()
        {
            var capture = _root.MaterializeBackup(await PlanFullAsync());
            _root.Probe.Target = _root.Probe.Target with { IsProcessRunning = true };

            await AssertFailureAsync(
                "restore-target-running",
                () => _root.CreateProvider().PlanRestoreAsync(
                    Guid.NewGuid(),
                    capture,
                    _root.TargetIdentity.NodeId,
                    CancellationToken.None));
        }

        [Test]
        public async Task PlanRestoreAsync_WithMixedNodeInventory_FailsClosed()
        {
            var capture = _root.MaterializeBackup(await PlanFullAsync());
            var mixed = capture with
            {
                Inventory = capture.Inventory.Select((file, index) => index == 0
                    ? file with { SourceNodeId = "source-node-b" }
                    : file).ToArray()
            };

            await AssertFailureAsync(
                "backup-node-mismatch",
                () => _root.CreateProvider().PlanRestoreAsync(
                    Guid.NewGuid(),
                    mixed,
                    _root.TargetIdentity.NodeId,
                    CancellationToken.None));
        }

        [Test]
        public async Task PlanRestoreAsync_WithCorruptArtifact_FailsChecksumValidation()
        {
            var capture = _root.MaterializeBackup(await PlanFullAsync());
            await File.AppendAllTextAsync(
                Path.Combine(capture.ArtifactRoot, "db", "writer.chk"),
                "corrupt");

            await AssertFailureAsync(
                "backup-checksum-mismatch",
                () => _root.CreateProvider().PlanRestoreAsync(
                    Guid.NewGuid(),
                    capture,
                    _root.TargetIdentity.NodeId,
                    CancellationToken.None));
        }

        [Test]
        public async Task Planning_WithCancelledToken_PropagatesCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await FluentActions.Awaiting(() => _root.CreateProvider().PlanCaptureAsync(
                    EventStoreDbCaptureKind.FullFileCopy,
                    Guid.NewGuid(),
                    null,
                    cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        private Task<EventStoreDbCapturePlan> PlanFullAsync() => _root.CreateProvider().PlanCaptureAsync(
            EventStoreDbCaptureKind.FullFileCopy,
            Guid.NewGuid(),
            null,
            CancellationToken.None);

        private static async Task AssertFailureAsync(string code, Func<Task> action)
        {
            var exception = await FluentActions.Awaiting(action)
                .Should().ThrowAsync<EventStoreDbProtectionException>();
            exception.Which.Code.Should().Be(code);
        }
    }
}
