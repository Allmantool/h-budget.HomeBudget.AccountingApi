using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    [TestFixture]
    internal sealed class PortabilityContractSampleIntegrityTests
    {
        private PortabilityContractFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new PortabilityContractFixture();
        }

        [Test]
        public void PackageSchemas_WhenComparedWithNormativeSchemas_AreByteIdentical()
        {
            var root = _fixture.ContractPath("schemas");
            var names = Directory.EnumerateFiles(root, "*.schema.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            names.Should().HaveCount(10);
            foreach (var name in names)
            {
                var normative = PortabilityContractFixture.Sha256(Path.Combine(root, name));
                var packaged = PortabilityContractFixture.Sha256(_fixture.SamplePath($"schemas/{name}"));
                packaged.Should().Be(normative, $"packaged schema '{name}' must match the normative v1 schema");
            }
        }

        [Test]
        public void Reconciliation_WhenComparedWithDataEntries_HasMatchingCounts()
        {
            var reconciliation = _fixture.ReadSampleObject("reconciliation.json");
            var counts = reconciliation["counts"] !.AsObject();
            var operations = _fixture.ReadSampleLines("operations.jsonl");
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["accounts"] = "accounts.jsonl",
                ["currencies"] = "currencies.jsonl",
                ["categories"] = "categories.jsonl",
                ["contractors"] = "contractors.jsonl",
                ["rates"] = "rates.jsonl",
                ["lineage"] = "lineage.jsonl"
            };

            foreach (var entry in expected)
            {
                counts[entry.Key] !.GetValue<int>().Should().Be(_fixture.ReadSampleLines(entry.Value).Count);
            }

            counts["settings"] !.GetValue<int>().Should().Be(1);
            counts["operations"] !.GetValue<int>().Should().Be(operations.Count);
            counts["openingBalances"] !.GetValue<int>().Should().Be(CountOperations(operations, "opening-balance"));
            counts["incomeOperations"] !.GetValue<int>().Should().Be(CountOperations(operations, "income"));
            counts["expenseOperations"] !.GetValue<int>().Should().Be(CountOperations(operations, "expense"));
            counts["adjustmentOperations"] !.GetValue<int>().Should().Be(CountOperations(operations, "adjustment"));
            counts["transferOperations"] !.GetValue<int>().Should().Be(CountOperations(operations, "transfer"));
            counts["splitLines"] !.GetValue<int>().Should().Be(operations.Sum(OperationSplitCount));

            var identityPayload = string.Join(
                '\n',
                operations.Select(operation => Text(operation, "id")).OrderBy(id => id, StringComparer.Ordinal)) + '\n';
            reconciliation["operationIdentitySha256"] !.GetValue<string>()
                .Should().Be(PortabilityContractFixture.Sha256Utf8(identityPayload));
        }

        [Test]
        public void FullSample_WhenDependencyClosureIsChecked_HasNoOrphans()
        {
            var currencies = ById("currencies.jsonl");
            var categories = ById("categories.jsonl");
            var contractors = ById("contractors.jsonl");
            var accounts = ById("accounts.jsonl");
            var operations = ById("operations.jsonl");
            var rates = ById("rates.jsonl");
            var lineage = ById("lineage.jsonl");

            foreach (var account in accounts.Values)
            {
                currencies.Should().ContainKey(Text(account, "currencyId"));
                operations.Should().ContainKey(Text(account, "openingBalanceOperationId"));
            }

            foreach (var category in categories.Values)
            {
                AssertOptional(category, "parentId", categories.Keys);
            }

            foreach (var operation in operations.Values)
            {
                AssertOperation(operation, accounts.Keys, currencies.Keys, categories.Keys, contractors.Keys, rates.Keys);
                AssertOptional(operation, "lineageId", lineage.Keys);
            }

            foreach (var rate in rates.Values)
            {
                currencies.Should().ContainKey(Text(rate, "baseCurrencyId"));
                currencies.Should().ContainKey(Text(rate, "quoteCurrencyId"));
            }

            var settings = _fixture.ReadSampleObject("settings.json");
            currencies.Should().ContainKey(Text(settings, "defaultCurrencyId"));
            AssertLineage(lineage.Values, accounts.Keys, categories.Keys, contractors.Keys, operations.Keys, rates.Keys);
        }

        private Dictionary<string, JsonObject> ById(string path) => _fixture.ReadSampleLines(path)
            .ToDictionary(value => Text(value, "id"), StringComparer.Ordinal);

        private static void AssertOperation(
            JsonObject operation,
            IEnumerable<string> accounts,
            IEnumerable<string> currencies,
            IEnumerable<string> categories,
            IEnumerable<string> contractors,
            IEnumerable<string> rates)
        {
            if (Text(operation, "kind") == "transfer")
            {
                AssertSide(operation["source"] !.AsObject(), accounts, currencies);
                AssertSide(operation["destination"] !.AsObject(), accounts, currencies);
                AssertOptional(operation["exchangeRate"] !.AsObject(), "rateId", rates);
                var fee = operation["fee"] !.AsObject();
                AssertSide(fee, accounts, currencies);
                categories.Should().Contain(Text(fee, "categoryId"));
                return;
            }

            accounts.Should().Contain(Text(operation, "accountId"));
            currencies.Should().Contain(Text(operation, "currencyId"));
            AssertOptional(operation, "categoryId", categories);
            AssertOptional(operation, "contractorId", contractors);
            foreach (var split in operation["splits"] !.AsArray())
            {
                categories.Should().Contain(Text(split!.AsObject(), "categoryId"));
            }
        }

        private static void AssertSide(
            JsonObject side,
            IEnumerable<string> accounts,
            IEnumerable<string> currencies)
        {
            accounts.Should().Contain(Text(side, "accountId"));
            currencies.Should().Contain(Text(side, "currencyId"));
        }

        private static void AssertLineage(
            IEnumerable<JsonObject> entries,
            IEnumerable<string> accounts,
            IEnumerable<string> categories,
            IEnumerable<string> contractors,
            IEnumerable<string> operations,
            IEnumerable<string> rates)
        {
            var identities = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["account"] = accounts.ToHashSet(StringComparer.Ordinal),
                ["category"] = categories.ToHashSet(StringComparer.Ordinal),
                ["contractor"] = contractors.ToHashSet(StringComparer.Ordinal),
                ["operation"] = operations.ToHashSet(StringComparer.Ordinal),
                ["rate"] = rates.ToHashSet(StringComparer.Ordinal)
            };

            foreach (var entry in entries)
            {
                identities[Text(entry, "entityType")].Should().Contain(Text(entry, "entityId"));
            }
        }

        private static void AssertOptional(JsonObject value, string property, IEnumerable<string> knownIds)
        {
            if (value[property] is JsonValue reference && reference.TryGetValue<string>(out var id))
            {
                knownIds.Should().Contain(id);
            }
        }

        private static int CountOperations(IEnumerable<JsonObject> operations, string kind) =>
            operations.Count(operation => Text(operation, "kind") == kind);

        private static int OperationSplitCount(JsonObject operation) =>
            operation["splits"] is JsonArray splits ? splits.Count : 0;

        private static string Text(JsonObject value, string property) => value[property] !.GetValue<string>();
    }
}
