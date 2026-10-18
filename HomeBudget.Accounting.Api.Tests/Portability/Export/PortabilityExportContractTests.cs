using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    [TestFixture]
    internal sealed class PortabilityExportContractTests
    {
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
        public async Task ExportAsync_WhenProjectionStartsBehind_UsesOneSettledCommonCutoffForEveryReader()
        {
            _scenario.ProjectionStartsBehind = true;
            _scenario.ProjectionCanCatchUp = true;

            using var archive = await ExportAsync().ConfigureAwait(false);
            var manifest = archive.ReadJson("manifest.json");

            manifest.GetProperty("commonCutoffUtc").GetString()
                .Should().Be("2025-01-15T12:30:00Z");
            manifest.GetProperty("projectionWatermarkUtc").GetString()
                .Should().Be("2025-01-15T12:30:00Z");
            _scenario.Calls.Should().ContainInOrder(
                "AcquireCutoffAsync",
                "WaitForProjectionAsync",
                "ReadAccountsAsync");
            _scenario.ObservedCutoffSequences.Should().NotBeEmpty()
                .And.OnlyContain(value => value == 4242);
        }

        [Test]
        public async Task ExportAsync_WhenProjectionCannotReachCutoff_RejectsWithoutPublishingPartialArchive()
        {
            _scenario.ProjectionStartsBehind = true;
            _scenario.ProjectionCanCatchUp = false;

            var code = await _driver.ExportRejectedAsync(_scenario).ConfigureAwait(false);

            code.Should().Be("export.projection-lag");
            _root.AssertNoPublishedOrStagedArtifact();
        }

        [Test]
        public async Task ExportAsync_WhenLedgerHasAccounts_PreservesAccountsAndOpeningBalanceIdentities()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var accounts = archive.ReadJsonLines("accounts.jsonl");
            var operations = archive.ReadJsonLines("operations.jsonl");

            accounts.Should().HaveCount(2);
            var openingIds = accounts
                .Select(value => value.GetProperty("openingBalanceOperationId").GetString())
                .ToArray();
            operations
                .Where(value => value.GetProperty("kind").GetString() == "opening-balance")
                .Select(value => value.GetProperty("id").GetString())
                .Should().BeEquivalentTo(openingIds);
        }

        [Test]
        public async Task ExportAsync_WhenLedgerHasReferenceData_PreservesCurrenciesCategoriesAndContractors()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);

            archive.ReadJsonLines("currencies.jsonl").Should().Contain(value =>
                value.GetProperty("code").GetString() == "BYN");
            archive.ReadJsonLines("categories.jsonl").Should().Contain(value =>
                value.GetProperty("name").GetString() == "Food"
                && value.GetProperty("parentId").ValueKind != System.Text.Json.JsonValueKind.Null);
            archive.ReadJsonLines("contractors.jsonl").Should().Contain(value =>
                value.GetProperty("legacyReference").GetString() == "FamilyPro12:PAYEE:10");
        }

        [Test]
        public async Task ExportAsync_WhenSettingsAndRatesAreRequired_PreservesAllowlistedSettingsAndHistoricalRate()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var settings = archive.ReadJson("settings.json");
            var rates = archive.ReadJsonLines("rates.jsonl");

            settings.GetProperty("timeZone").GetString().Should().Be("Europe/Minsk");
            settings.GetProperty("defaultCurrencyId").GetString()
                .Should().Be("10000000-0000-4000-8000-000000000001");
            rates.Should().ContainSingle(value =>
                value.GetProperty("id").GetString() == "50000000-0000-4000-8000-000000000001"
                && value.GetProperty("rate").GetString() == "0.3");
        }

        [Test]
        public async Task ExportAsync_WhenLedgerHasFinancialKinds_PreservesIncomeExpenseAdjustmentAndOpeningBalance()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);

            archive.ReadJsonLines("operations.jsonl")
                .Select(value => value.GetProperty("kind").GetString())
                .Should().Contain(["income", "expense", "adjustment", "opening-balance"]);
        }

        [Test]
        public async Task ExportAsync_WhenTransferCrossesCurrencies_PreservesCanonicalIdentitySidesRateAndFee()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var transfer = archive.ReadJsonLines("operations.jsonl")
                .Single(value => value.GetProperty("kind").GetString() == "transfer");

            transfer.GetProperty("id").GetString()
                .Should().Be("60000000-0000-4000-8000-000000000006");
            transfer.GetProperty("source").GetProperty("amount").GetString().Should().Be("300.00");
            transfer.GetProperty("destination").GetProperty("amount").GetString().Should().Be("90.00");
            transfer.GetProperty("exchangeRate").GetProperty("value").GetString().Should().Be("0.3");
            transfer.GetProperty("fee").GetProperty("amount").GetString().Should().Be("2.00");
        }

        [Test]
        public async Task ExportAsync_WhenExpenseIsSplit_PreservesParentAndExactSplitLines()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var split = archive.ReadJsonLines("operations.jsonl")
                .Single(value => value.TryGetProperty("splits", out var lines)
                    && lines.GetArrayLength() == 2
                    && !value.GetProperty("archived").GetBoolean());

            split.GetProperty("amount").GetString().Should().Be("125.45");
            split.GetProperty("splits").EnumerateArray()
                .Select(value => value.GetProperty("amount").GetString())
                .Should().Equal("100.00", "25.45");
        }

        [Test]
        public async Task ExportAsync_WhenRecordsWereArchivedOrDeleted_PreservesExplicitTombstoneSemantics()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var archivedContractor = archive.ReadJsonLines("contractors.jsonl")
                .Single(value => value.GetProperty("archived").GetBoolean());
            var archivedOperation = archive.ReadJsonLines("operations.jsonl")
                .Single(value => value.GetProperty("archived").GetBoolean());

            archivedContractor.GetProperty("archivedUtc").GetString()
                .Should().Be("2025-01-20T10:00:00Z");
            archivedOperation.GetProperty("archivedUtc").GetString()
                .Should().Be("2025-01-15T12:20:00Z");
            archivedOperation.GetProperty("note").GetString().Should().Contain("tombstone");
        }

        [Test]
        public async Task ExportAsync_WhenFamilyProEntitiesExist_PreservesRunBatchMappingAndLogicalLineage()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var lineage = archive.ReadJsonLines("lineage.jsonl");

            lineage.Should().Contain(value =>
                value.GetProperty("sourceSystem").GetString() == "FamilyPro12"
                && value.GetProperty("sourceRunId").GetString() == "synthetic-run-001"
                && value.GetProperty("sourceBatchId").GetString() == "synthetic-batch-002"
                && value.GetProperty("mappingId").GetString() == "operation-map-100");
        }

        [Test]
        public async Task ExportAsync_WhenValuesAreSerialized_UsesExactDecimalDateOnlyAndUtcStringForms()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var operations = archive.ReadJsonLines("operations.jsonl");

            operations.SelectMany(DecimalStrings).Should().OnlyContain(value =>
                Regex.IsMatch(value, "^(0|[1-9][0-9]*)(\\.[0-9]+)?$", RegexOptions.CultureInvariant));
            operations.Select(value => value.GetProperty("businessDate").GetString())
                .All(IsDateOnly)
                .Should().BeTrue();
            operations.Select(value => value.GetProperty("originalCreatedUtc").GetString())
                .Should().OnlyContain(value => value.EndsWith('Z'));
        }

        [Test]
        public async Task ExportAsync_WhenPartialTransferRequested_IncludesOnlyTransferDependencyClosure()
        {
            _scenario.Completeness = "partial";
            _scenario.SelectedOperationIds = ["60000000-0000-4000-8000-000000000006"];

            using var archive = await ExportAsync().ConfigureAwait(false);
            var manifest = archive.ReadJson("manifest.json");
            var operations = archive.ReadJsonLines("operations.jsonl");

            manifest.GetProperty("completeness").GetString().Should().Be("partial");
            operations.Should().ContainSingle(value =>
                value.GetProperty("id").GetString() == _scenario.SelectedOperationIds[0]);
            archive.ReadJsonLines("accounts.jsonl").Should().HaveCount(2);
            archive.ReadJsonLines("currencies.jsonl").Should().HaveCount(2);
            archive.ReadJsonLines("rates.jsonl").Should().ContainSingle();
            archive.ReadJsonLines("categories.jsonl").Should().ContainSingle(value =>
                value.GetProperty("id").GetString() == "20000000-0000-4000-8000-000000000005");
        }

        [Test]
        public async Task ExportAsync_WhenSourceContainsInfrastructureSecrets_ExportsOnlyBusinessAllowlist()
        {
            using var archive = await ExportAsync().ConfigureAwait(false);
            var allText = archive.ReadAllText();
            var normalized = allText.ToUpperInvariant();

            normalized.Should().NotContain("CONNECTIONSTRING")
                .And.NotContain("PASSWORD=")
                .And.NotContain("DO-NOT-EXPORT")
                .And.NotContain("SESSIONTOKEN")
                .And.NotContain("ENCRYPTIONKEY");
        }

        private static bool IsDateOnly(string value) => DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

        private static string[] DecimalStrings(System.Text.Json.JsonElement operation)
        {
            if (operation.GetProperty("kind").GetString() == "transfer")
            {
                return
                [
                    operation.GetProperty("source").GetProperty("amount").GetString(),
                    operation.GetProperty("destination").GetProperty("amount").GetString(),
                    operation.GetProperty("exchangeRate").GetProperty("value").GetString(),
                    operation.GetProperty("fee").GetProperty("amount").GetString()
                ];
            }

            return [operation.GetProperty("amount").GetString()];
        }

        private async Task<PortabilityExportArchive> ExportAsync()
        {
            await _driver.ExportAsync(_scenario).ConfigureAwait(false);
            return new PortabilityExportArchive(_root.DestinationPath);
        }
    }
}
