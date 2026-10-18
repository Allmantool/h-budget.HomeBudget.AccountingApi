using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    [TestFixture]
    internal sealed class PortabilityContractTests
    {
        private PortabilityContractFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new PortabilityContractFixture();
        }

        [Test]
        public void FullSample_WhenComparedWithCatalog_ContainsExactlyRequiredEntries()
        {
            var catalog = _fixture.ReadContractObject("entry-catalog.json");
            var requiredEntries = catalog["entries"] !.AsArray()
                .Where(entry => entry!["requiredForFull"] !.GetValue<bool>())
                .Select(entry => entry!["path"] !.GetValue<string>())
                .OrderBy(path => path, StringComparer.Ordinal);

            _fixture.SampleEntries().Should().Equal(requiredEntries);
            requiredEntries.Should().NotContain("attachments.jsonl");
            catalog["unknownEntries"] !.GetValue<string>().Should().Be("reject");
        }

        [Test]
        public void FullSample_WhenValidated_EveryStructuredRecordConformsToItsSchema()
        {
            var catalog = _fixture.ReadContractObject("entry-catalog.json");
            var failures = new List<string>();

            foreach (var entry in catalog["entries"] !.AsArray())
            {
                var kind = entry!["kind"] !.GetValue<string>();
                if (kind is not "json" and not "jsonl")
                {
                    continue;
                }

                var path = entry["path"] !.GetValue<string>();
                var schema = entry["schema"] !.GetValue<string>();
                var instances = kind == "json"
                    ? [_fixture.ReadSampleObject(path)]
                    : _fixture.ReadSampleLines(path);

                foreach (var instance in instances)
                {
                    if (!_fixture.Evaluate(schema, instance).IsValid)
                    {
                        failures.Add($"{path} does not satisfy {schema}");
                    }
                }
            }

            failures.Should().BeEmpty();
        }

        [Test]
        public void Manifest_WhenVerified_MatchesSampleBytesCountsAndAttachmentPolicy()
        {
            var manifest = _fixture.ReadSampleObject("manifest.json");
            manifest["format"] !.GetValue<string>().Should().Be("home-ledger-portability");
            manifest["formatVersion"] !.GetValue<string>().Should().Be("1.0.0");
            manifest["attachments"] !["status"] !.GetValue<string>().Should().Be("unsupported-not-present");

            var inventoryPaths = manifest["files"] !.AsArray()
                .Select(file => file!["path"] !.GetValue<string>())
                .OrderBy(path => path, StringComparer.Ordinal);
            _fixture.SampleEntries()
                .Where(path => path != "manifest.json")
                .Should().Equal(inventoryPaths);

            foreach (var file in manifest["files"] !.AsArray())
            {
                var path = file!["path"] !.GetValue<string>();
                var fullPath = _fixture.SamplePath(path);
                File.Exists(fullPath).Should().BeTrue($"manifest entry '{path}' must exist");
                file["byteSize"] !.GetValue<long>().Should().Be(new FileInfo(fullPath).Length);
                file["sha256"] !.GetValue<string>().Should().Be(PortabilityContractFixture.Sha256(fullPath));

                if (path.EndsWith(".jsonl", StringComparison.Ordinal))
                {
                    file["recordCount"] !.GetValue<int>().Should().Be(File.ReadLines(fullPath).Count());
                }
            }
        }

        [Test]
        public void CompatibilityMatrix_WhenRead_DefinesStrictV1ImportPolicy()
        {
            var matrix = _fixture.ReadContractObject("compatibility-matrix.json");
            matrix["currentVersion"] !.GetValue<string>().Should().Be("1.0.0");
            matrix["supportedImportVersions"] !.AsArray()
                .Select(value => value!.GetValue<string>())
                .Should().Equal("1.0.x");
            matrix["unknownVersionPolicy"] !.GetValue<string>().Should().Be("reject");
            matrix["partialReplacement"] !.GetValue<string>().Should().Be("rejected");
            matrix["historyRecreation"] !.GetValue<string>().Should().Be("not-claimed");
        }

        [Test]
        public void ManifestSchema_WhenCatalogAllowsAnalysisCsv_AcceptsDocumentedCsvMediaType()
        {
            var catalog = _fixture.ReadContractObject("entry-catalog.json");
            var csvPaths = catalog["entries"] !.AsArray()
                .Where(entry => entry!["kind"] !.GetValue<string>() == "csv")
                .Select(entry => entry!["path"] !.GetValue<string>())
                .ToArray();
            var manifest = _fixture.ReadSampleObject("manifest.json");
            manifest["files"] !.AsArray().Add(
                new JsonObject
                {
                    ["path"] = csvPaths[0],
                    ["mediaType"] = "text/csv",
                    ["byteSize"] = 1,
                    ["sha256"] = new string('0', 64)
                });

            csvPaths.Should().NotBeEmpty();
            catalog["unknownEntries"] !.GetValue<string>().Should().Be("reject");
            _fixture.Evaluate("schemas/manifest.schema.json", manifest).IsValid.Should().BeTrue();
        }
    }
}
