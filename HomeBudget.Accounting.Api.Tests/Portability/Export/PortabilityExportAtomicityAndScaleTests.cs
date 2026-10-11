using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    [TestFixture]
    [NonParallelizable]
    internal sealed class PortabilityExportAtomicityAndScaleTests
    {
        private const int RepresentativeOperationCount = 50_000;
        private const long MaximumManagedMemoryGrowthBytes = 64L * 1024 * 1024;
        private DisposablePortabilityExportRoot _root;
        private PortabilityExportContractDriver _driver;
        private PortabilityExportScenario _scenario;

        [SetUp]
        public void SetUp()
        {
            _root = new DisposablePortabilityExportRoot();
            _driver = new PortabilityExportContractDriver();
            _scenario = new PortabilityExportScenario(
                new PortabilityContractFixture(),
                _root.DestinationPath);
        }

        [TearDown]
        public void TearDown() => _root.Dispose();

        [Test]
        public async Task ExportAsync_WhenSourceFailsMidStream_LeavesNoPublishedOrStagedArtifact()
        {
            _scenario.ThrowOnReader = "ReadOperationsAsync";

            var code = await _driver.ExportRejectedAsync(_scenario).ConfigureAwait(false);

            code.Should().Be("export.source-read-failed");
            _root.AssertNoPublishedOrStagedArtifact();
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ExportAsync_WithFiftyThousandOperations_StreamsWithinMemoryBoundAndPublishesLast()
        {
            _scenario.GeneratedOperationCount = RepresentativeOperationCount;
            _scenario.ProjectionStartsBehind = false;
            _scenario.BeginMemoryObservation();
            var elapsed = Stopwatch.StartNew();

            await _driver.ExportAsync(_scenario).ConfigureAwait(false);
            elapsed.Stop();
            TestContext.Progress.WriteLine(
                $"50k export: elapsed={elapsed.ElapsedMilliseconds}ms; "
                + $"maximumManagedGrowth={_scenario.MaximumObservedManagedBytes} bytes");

            _scenario.DestinationObservedBeforeSourceCompletion.Should().BeFalse();
            _scenario.MaximumObservedManagedBytes.Should().BeLessThan(
                MaximumManagedMemoryGrowthBytes,
                "the deterministic export contract permits at most 64 MiB managed growth");
            File.Exists(_root.DestinationPath).Should().BeTrue();
            using var archive = new PortabilityExportArchive(_root.DestinationPath);
            archive.ReadJsonLines("operations.jsonl").Should().HaveCount(RepresentativeOperationCount);
            archive.ReadJson("manifest.json").GetProperty("files")
                .EnumerateArray()
                .Should().Contain(value =>
                    value.GetProperty("path").GetString() == "operations.jsonl"
                    && value.GetProperty("recordCount").GetInt32() == RepresentativeOperationCount);
        }
    }
}
