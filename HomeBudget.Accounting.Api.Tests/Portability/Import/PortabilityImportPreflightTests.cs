using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    [TestFixture]
    internal sealed class PortabilityImportPreflightTests
    {
        private DisposablePortabilityImportPackage _package;
        private PortabilityImportScenario _scenario;
        private PortabilityImportPreflightDriver _driver;

        [SetUp]
        public void SetUp()
        {
            _package = new DisposablePortabilityImportPackage();
            _scenario = new PortabilityImportScenario(_package);
            _driver = new PortabilityImportPreflightDriver();
        }

        [TearDown]
        public void TearDown() => _package.Dispose();

        [Test]
        public async Task ValidateOnly_WhenPackageIsValid_PerformsNoTargetMutation()
        {
            await _driver.ValidateAsync(_scenario).ConfigureAwait(false);

            _scenario.ProbeCalls.Should().Be(1);
            _scenario.TargetMutationAttempts.Should().Be(0);
        }

        [TestCase("merge", "import.mode-unsupported")]
        [TestCase("empty-target", "import.target-not-empty")]
        public async Task Preflight_WhenModeOrTargetIsUnsafe_Rejects(string mode, string code)
        {
            _scenario.Mode = mode;
            _scenario.HasBusinessState = mode == "empty-target";

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code.Should().Be(code);
            _scenario.TargetMutationAttempts.Should().Be(0);
        }

        [Test]
        public async Task Replace_WhenPackageIsPartial_RejectsBeforeActivation()
        {
            _scenario.Mode = "replace";
            _scenario.MaintenanceIsolationConfirmed = true;
            _scenario.PreOperationRecoverySetId = Guid.NewGuid().ToString("D");
            _package.MutateManifest(value => value["completeness"] = "partial");

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.partial-replacement");
        }

        [Test]
        public async Task Replace_WhenTargetGenerationIsActive_RejectsBeforeMutation()
        {
            _scenario.Mode = "replace";
            _scenario.IsActive = true;
            _scenario.MaintenanceIsolationConfirmed = true;
            _scenario.PreOperationRecoverySetId = Guid.NewGuid().ToString("D");

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.replacement-prerequisite");
            _scenario.TargetMutationAttempts.Should().Be(0);
        }

        [Test]
        public async Task Preflight_WhenVersionIsUnsupported_RejectsExplicitly()
        {
            _package.MutateManifest(value => value["formatVersion"] = "2.0.0");

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.version-unsupported");
        }

        [Test]
        public async Task Preflight_WhenInventoryChecksumDiffers_ReportsExactFile()
        {
            _package.MutateJsonLine("accounts.jsonl", 0, value => value["name"] = "changed", false);

            var error = await _driver.RejectAsync(_scenario).ConfigureAwait(false);

            error.Code.Should().Be("import.checksum-mismatch");
            error.EntryPath.Should().Be("accounts.jsonl");
        }

        [Test]
        public async Task Preflight_WhenCapacityIsInsufficient_RejectsBeforeMutation()
        {
            _scenario.AvailableBytes = 1;

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.capacity-insufficient");
        }

        [Test]
        public async Task Preflight_WhenIdentityIsDuplicated_ReportsFileAndEntity()
        {
            var line = File.ReadLines(Path.Combine(_package.RootPath, "accounts.jsonl")).First();
            _package.AppendJsonLine("accounts.jsonl", line);

            var error = await _driver.RejectAsync(_scenario).ConfigureAwait(false);

            error.Code.Should().Be("import.identity-duplicate");
            error.EntryPath.Should().Be("accounts.jsonl");
            error.EntityId.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public async Task Preflight_WhenReferenceIsMissing_ReportsEntityAndField()
        {
            _package.MutateJsonLine(
                "operations.jsonl",
                2,
                value => value["accountId"] = "ffffffff-ffff-4fff-8fff-ffffffffffff");

            var error = await _driver.RejectAsync(_scenario).ConfigureAwait(false);

            error.Code.Should().Be("import.reference-missing");
            error.EntryPath.Should().Be("operations.jsonl");
            error.EntityId.Should().Be("60000000-0000-4000-8000-000000000003");
            error.FieldPath.Should().Be("accountId");
        }

        [Test]
        public async Task Preflight_WhenAmountExceedsCurrencyPrecision_RejectsWithoutRounding()
        {
            _package.MutateJsonLine("operations.jsonl", 3, value => value["amount"] = "125.451");

            var error = await _driver.RejectAsync(_scenario).ConfigureAwait(false);

            error.Code.Should().Be("import.currency-precision");
            error.FieldPath.Should().Be("amount");
        }

        [Test]
        public async Task Preflight_WhenTransferUsesSameAccountOnBothSides_RejectsInvariant()
        {
            _package.MutateJsonLine(
                "operations.jsonl",
                5,
                value => value["destination"]["accountId"] = value["source"]["accountId"].GetValue<string>());

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.transfer-invariant");
        }

        [Test]
        public async Task Preflight_WhenSplitTotalDiffersFromParent_RejectsInvariant()
        {
            _package.MutateJsonLine(
                "operations.jsonl",
                3,
                value => value["splits"].AsArray()[0]["amount"] = "99.99");

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.split-invariant");
        }

        [Test]
        public async Task Preflight_WhenOpeningBalanceIsDebit_RejectsFinancialInvariant()
        {
            _package.MutateJsonLine("operations.jsonl", 0, value => value["direction"] = "debit");

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.financial-invariant");
        }

        [Test]
        public async Task Resume_WhenSourceBytesChanged_RejectsImmutableBinding()
        {
            _scenario.Mode = "resume";
            _scenario.ExpectedPackageSha256 = new string('a', 64);

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.source-binding-conflict");
        }

        [Test]
        public async Task Resume_WhenTargetGenerationChanged_RejectsImmutableBinding()
        {
            _scenario.Mode = "resume";
            _scenario.ExpectedTargetGenerationId = "new-generation";

            (await _driver.RejectAsync(_scenario).ConfigureAwait(false)).Code
                .Should().Be("import.target-binding-conflict");
        }
    }
}
