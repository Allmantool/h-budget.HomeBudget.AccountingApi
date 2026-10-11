using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    [TestFixture]
    internal sealed class PortabilityImportReconciliationTests
    {
        private DisposablePortabilityImportPackage _package;
        private InMemoryPortabilityImportJournalStore _journal;
        private RecordingPortabilityImportApplicationAdapter _executionAdapter;
        private RecordingPortabilityImportReadbackAdapter _readback;
        private PortabilityImportPreflightService _preflight;
        private PortabilityImportExecutionRequest _executionRequest;

        [SetUp]
        public async Task SetUp()
        {
            _package = new DisposablePortabilityImportPackage();
            _journal = new InMemoryPortabilityImportJournalStore();
            _executionAdapter = new RecordingPortabilityImportApplicationAdapter();
            _readback = new RecordingPortabilityImportReadbackAdapter();
            _preflight = new PortabilityImportPreflightService(
                new ReconciliationTargetProbe(),
                new PortabilityImportPreflightOptions());
            _executionRequest = ExecutionRequest();
            var execution = new PortabilityImportExecutionService(
                _preflight,
                _journal,
                _executionAdapter,
                new PortabilityImportExecutionOptions { BatchSize = 3 });
            var result = await execution.ExecuteAsync(
                _executionRequest,
                _package.RootPath,
                CancellationToken.None);
            result.State.Should().Be("awaiting-reconciliation");
            _readback.Snapshot = ExactSnapshot();
        }

        [TearDown]
        public void TearDown() => _package.Dispose();

        [Test]
        public async Task ReconcileAsync_ExactReadback_ValidatesAllPortableFinancialAndReferenceContracts()
        {
            var records = _readback.Snapshot.Records;
            records.Select(value => value.EntityType).Should().Contain(
                ["accounts", "currencies", "categories", "contractors", "settings", "rates", "operations", "lineage"]);
            var operations = records.Where(value => value.EntityType == "operations")
                .Select(value => value.Payload).ToArray();
            operations.Select(value => value.GetProperty("kind").GetString()).Should().Contain(
                ["opening-balance", "income", "expense", "adjustment", "transfer"]);
            operations.Should().Contain(value => value.GetProperty("splits").GetArrayLength() > 0);
            var transfer = operations.Single(value => value.GetProperty("kind").GetString() == "transfer");
            transfer.GetProperty("source").GetProperty("amount").GetString().Should().NotBe(
                transfer.GetProperty("destination").GetProperty("amount").GetString());
            transfer.GetProperty("exchangeRate").GetProperty("rateId").GetString().Should().NotBeNullOrWhiteSpace();
            transfer.GetProperty("fee").GetProperty("amount").GetString().Should().Be("2.00");

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.State.Should().Be("succeeded");
            result.EvidenceSha256.Should().MatchRegex("^[0-9a-f]{64}$");
            _readback.Calls.Should().Be(1);
            (await _journal.GetAsync(_executionRequest.OperationId, CancellationToken.None)).State
                .Should().Be("succeeded");
        }

        [Test]
        public async Task ReconcileAsync_WhenProjectionIsMissing_RemainsUnsuccessfulWithIdentityEvidence()
        {
            var records = _readback.Snapshot.Records.Skip(1).ToArray();
            _readback.Snapshot = _readback.Snapshot with { Records = records };

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.State.Should().Be("failed");
            result.ErrorCode.Should().Be("import.reconciliation-missing-entity");
            result.EntityId.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public async Task ReconcileAsync_WhenPeriodCurrencyTotalDiffers_FailsWithTotalsEvidence()
        {
            var summary = JsonNode.Parse(_readback.Snapshot.Summary.GetRawText()).AsObject();
            summary["periodTotals"].AsArray()[0]["fees"] = "999.00";
            _readback.Snapshot = _readback.Snapshot with { Summary = Element(summary) };

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.State.Should().Be("failed");
            result.ErrorCode.Should().Be("import.reconciliation-totals-mismatch");
        }

        [Test]
        public async Task ReconcileAsync_WhenPerAccountCurrencyBalanceDiffers_FailsWithBalanceEvidence()
        {
            var summary = JsonNode.Parse(_readback.Snapshot.Summary.GetRawText()).AsObject();
            summary["accountBalances"].AsArray()[0]["balance"] = "0.00";
            _readback.Snapshot = _readback.Snapshot with { Summary = Element(summary) };

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.ErrorCode.Should().Be("import.reconciliation-balances-mismatch");
        }

        [Test]
        public async Task ReconcileAsync_WhenReadbackReferenceIsDangling_FailsWithOrphanReferenceEvidence()
        {
            var records = _readback.Snapshot.Records.ToArray();
            var index = Array.FindIndex(records, value => value.EntityType == "accounts");
            var payload = JsonNode.Parse(records[index].Payload.GetRawText()).AsObject();
            payload["currencyId"] = "ffffffff-ffff-4fff-8fff-ffffffffffff";
            records[index] = records[index] with { Payload = Element(payload) };
            _readback.Snapshot = _readback.Snapshot with { Records = records };

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.ErrorCode.Should().Be("import.reconciliation-orphan-reference");
            result.EntityType.Should().Be("accounts");
        }

        [Test]
        public async Task ReconcileAsync_WhenReadbackContainsDuplicate_FailsBeforeSuccess()
        {
            var records = _readback.Snapshot.Records.Append(_readback.Snapshot.Records[0]).ToArray();
            _readback.Snapshot = _readback.Snapshot with { Records = records };

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.ErrorCode.Should().Be("import.reconciliation-duplicate");
        }

        [Test]
        public async Task ReconcileAsync_WhenReadbackContainsOrphan_FailsBeforeSuccess()
        {
            var source = _readback.Snapshot.Records[0];
            var orphan = source with { SourceEntityId = "ffffffff-ffff-4fff-8fff-ffffffffffff" };
            var records = _readback.Snapshot.Records.Append(orphan).ToArray();
            _readback.Snapshot = _readback.Snapshot with { Records = records };

            var result = await Service().ReconcileAsync(Request(), _package.RootPath, CancellationToken.None);

            result.ErrorCode.Should().Be("import.reconciliation-orphan");
        }

        [Test]
        public async Task ExecuteAsync_HttpAcceptanceAlone_DoesNotCompleteImport()
        {
            var journal = await _journal.GetAsync(_executionRequest.OperationId, CancellationToken.None);

            journal.State.Should().Be("awaiting-reconciliation");
            journal.Outcomes.Should().OnlyContain(value => value.State == "projected");
        }

        private PortabilityImportReconciliationService Service() => new(
            _preflight,
            _journal,
            _readback,
            new PortabilityImportReconciliationOptions { MaximumRecords = 100 });

        private PortabilityImportReconciliationRequest Request() => new(
            _executionRequest.OperationId,
            _executionRequest.ActorId,
            _executionRequest.Import);

        private PortabilityImportReconciliationSnapshot ExactSnapshot()
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(_package.RootPath, "reconciliation.json")));
            var records = _executionAdapter.Batches.SelectMany(value => value).ToArray();
            return new PortabilityImportReconciliationSnapshot(records, document.RootElement.Clone());
        }

        private PortabilityImportExecutionRequest ExecutionRequest() => new()
        {
            OperationId = Guid.NewGuid(),
            ActorId = "reconciliation-test-actor",
            Import = new PortabilityImportRequest
            {
                Mode = "empty-target",
                ActorAuthorized = true,
                PackageSha256 = _package.PackageSha256,
                EnvironmentId = "disposable-unit-test",
                LedgerId = "default",
                TargetGenerationId = "reconciliation-generation",
                TargetGenerationNumber = 7
            }
        };

        private static JsonElement Element(JsonNode node)
        {
            using var document = JsonDocument.Parse(node.ToJsonString());
            return document.RootElement.Clone();
        }

        private sealed class ReconciliationTargetProbe : IPortabilityImportTargetProbe
        {
            public Task<PortabilityImportTargetSnapshot> ProbeAsync(
                PortabilityImportRequest request,
                CancellationToken cancellationToken) => Task.FromResult(new PortabilityImportTargetSnapshot
                {
                    EnvironmentId = request.EnvironmentId,
                    LedgerId = request.LedgerId,
                    GenerationId = request.TargetGenerationId,
                    GenerationNumber = request.TargetGenerationNumber,
                    HasBusinessState = request.Mode == "resume",
                    AvailableBytes = 1_000_000_000
                });
        }
    }
}
