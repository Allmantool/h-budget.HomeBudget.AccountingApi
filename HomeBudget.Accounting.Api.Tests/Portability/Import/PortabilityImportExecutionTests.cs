using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    [TestFixture]
    internal sealed class PortabilityImportExecutionTests
    {
        private DisposablePortabilityImportPackage _package;
        private InMemoryPortabilityImportJournalStore _journal;
        private RecordingPortabilityImportApplicationAdapter _adapter;
        private PortabilityImportPreflightService _preflight;
        private PortabilityImportExecutionOptions _options;
        private PortabilityImportExecutionRequest _request;

        [SetUp]
        public void SetUp()
        {
            _package = new DisposablePortabilityImportPackage();
            _journal = new InMemoryPortabilityImportJournalStore();
            _adapter = new RecordingPortabilityImportApplicationAdapter();
            _preflight = new PortabilityImportPreflightService(
                new ExecutionTargetProbe(),
                new PortabilityImportPreflightOptions());
            _options = new PortabilityImportExecutionOptions { BatchSize = 2 };
            _request = Request("empty-target");
        }

        [TearDown]
        public void TearDown() => _package.Dispose();

        [Test]
        public async Task ExecuteAsync_LoadsDependenciesBeforeOperationsAndLineage()
        {
            await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);

            var types = _adapter.Batches.SelectMany(value => value).Select(value => value.EntityType).ToList();
            First(types, "currencies").Should().BeLessThan(First(types, "accounts"));
            First(types, "categories").Should().BeLessThan(First(types, "operations"));
            First(types, "contractors").Should().BeLessThan(First(types, "operations"));
            First(types, "accounts").Should().BeLessThan(First(types, "operations"));
            First(types, "rates").Should().BeLessThan(First(types, "operations"));
            First(types, "operations").Should().BeLessThan(First(types, "lineage"));
        }

        [Test]
        public async Task ExecuteAsync_UsesConfiguredBoundedBatches()
        {
            await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);

            _adapter.Batches.Should().NotBeEmpty()
                .And.OnlyContain(batch => batch.Count <= _options.BatchSize);
        }

        [Test]
        public async Task ExecuteAsync_WhenCancelledAfterAcceptance_SettlesAcceptedBatchAndStopsNewWork()
        {
            using var cancellation = new CancellationTokenSource();
            _adapter.ReturnAcceptedBeforeSettlement = true;
            _adapter.AfterFirstBatch = cancellation.Cancel;

            var result = await Service().ExecuteAsync(_request, _package.RootPath, cancellation.Token);

            result.State.Should().Be("cancelled");
            result.AcceptedCount.Should().Be(result.SettledCount).And.BeGreaterThan(0);
            result.RemainingCount.Should().BeGreaterThan(0);
            _adapter.Batches.Should().ContainSingle();
        }

        [Test]
        public async Task ExecuteAsync_WhenResponseTimesOutAfterAcceptance_ResolvesStatusWithoutDuplicateEffect()
        {
            _adapter.TimeoutAfterAcceptanceOnce = true;

            var result = await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);

            result.State.Should().Be("awaiting-reconciliation");
            result.RemainingCount.Should().Be(0);
            _adapter.EffectCount.Should().Be(result.SettledCount);
        }

        [Test]
        public async Task ExecuteAsync_WhenIdenticalRunIsRetried_ProducesZeroDuplicateEffect()
        {
            var first = await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);
            var effects = _adapter.EffectCount;

            var second = await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);

            second.Should().Be(first);
            _adapter.EffectCount.Should().Be(effects);
        }

        [Test]
        public async Task ExecuteAsync_WhenSameRunUsesChangedPackage_ReturnsBindingConflict()
        {
            await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);
            _package.MutateJsonLine("contractors.jsonl", 0, value => value["name"] = "Changed");
            _request = Request("empty-target");

            var action = async () => await Service().ExecuteAsync(
                _request,
                _package.RootPath,
                CancellationToken.None);

            (await action.Should().ThrowAsync<PortabilityImportJournalConflictException>())
                .Which.Code.Should().Be("import.journal-binding-conflict");
        }

        [Test]
        public async Task ExecuteAsync_WhenProcessCrashesAfterAcceptance_ResumeSettlesWithoutDuplicateEffect()
        {
            _adapter.CrashAfterAcceptanceOnce = true;
            var service = Service();

            await FluentActions.Invoking(() => service.ExecuteAsync(
                    _request,
                    _package.RootPath,
                    CancellationToken.None))
                .Should().ThrowAsync<SimulatedImportProcessCrashException>();
            var effects = _adapter.EffectCount;
            _request = ResumeRequest();

            var result = await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);

            result.State.Should().Be("awaiting-reconciliation");
            result.RemainingCount.Should().Be(0);
            _adapter.EffectCount.Should().BeGreaterThanOrEqualTo(effects);
            _adapter.EffectCount.Should().Be(result.SettledCount);
        }

        [Test]
        public async Task ExecuteAsync_WhenAdapterReportsChangedFingerprint_StopsWithConflict()
        {
            await Service().ExecuteAsync(_request, _package.RootPath, CancellationToken.None);
            var effects = _adapter.EffectCount;
            var record = _adapter.Batches.SelectMany(value => value).First();
            var conflicting = record with { Fingerprint = new string('f', 64) };

            var result = await _adapter.AcceptBatchAsync(
                [conflicting],
                Context(),
                CancellationToken.None);

            result.Should().ContainSingle(value => value.Status == PortabilityImportRecordStatuses.Conflict);
            _adapter.EffectCount.Should().Be(effects);
        }

        [Test]
        public async Task ExecuteAsync_ReplaceInactiveGeneration_StopsBeforeReconciliationAndActivation()
        {
            _request = Request("replace");
            _request.Import.MaintenanceIsolationConfirmed = true;
            _request.Import.PreOperationRecoverySetId = Guid.NewGuid().ToString("D");

            var result = await Service().ExecuteAsync(
                _request,
                _package.RootPath,
                CancellationToken.None);

            result.State.Should().Be("awaiting-reconciliation");
            result.RemainingCount.Should().Be(0);
        }

        private PortabilityImportExecutionService Service() => new(
            _preflight,
            _journal,
            _adapter,
            _options);

        private PortabilityImportExecutionRequest Request(string mode) => new()
        {
            OperationId = _request?.OperationId ?? Guid.NewGuid(),
            ActorId = "unit-test-actor",
            Import = new PortabilityImportRequest
            {
                Mode = mode,
                ActorAuthorized = true,
                PackageSha256 = _package.PackageSha256,
                EnvironmentId = "disposable-unit-test",
                LedgerId = "default",
                TargetGenerationId = "import-generation",
                TargetGenerationNumber = 1,
                MaintenanceIsolationConfirmed = false
            }
        };

        private PortabilityImportExecutionRequest ResumeRequest() => new()
        {
            OperationId = _request.OperationId,
            ActorId = _request.ActorId,
            Import = new PortabilityImportRequest
            {
                Mode = "resume",
                ResumeMode = "empty-target",
                ActorAuthorized = true,
                PackageSha256 = _package.PackageSha256,
                ExpectedPackageSha256 = _package.PackageSha256,
                EnvironmentId = "disposable-unit-test",
                LedgerId = "default",
                TargetGenerationId = "import-generation",
                TargetGenerationNumber = 1,
                ExpectedTargetGenerationId = "import-generation"
            }
        };

        private static PortabilityImportExecutionContext Context() => new(
            "disposable-unit-test",
            "default",
            "import-generation",
            1,
            "empty-target");

        private static int First(IReadOnlyList<string> values, string value)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] == value)
                {
                    return index;
                }
            }

            return -1;
        }

        private sealed class ExecutionTargetProbe : IPortabilityImportTargetProbe
        {
            public Task<PortabilityImportTargetSnapshot> ProbeAsync(
                PortabilityImportRequest request,
                CancellationToken cancellationToken) => Task.FromResult(new PortabilityImportTargetSnapshot
                {
                    EnvironmentId = request.EnvironmentId,
                    LedgerId = request.LedgerId,
                    GenerationId = request.TargetGenerationId,
                    GenerationNumber = request.TargetGenerationNumber,
                    HasBusinessState = false,
                    AvailableBytes = 1_000_000_000
                });
        }
    }
}
