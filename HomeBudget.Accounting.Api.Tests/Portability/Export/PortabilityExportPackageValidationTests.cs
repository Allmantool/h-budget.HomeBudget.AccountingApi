using System;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    [TestFixture]
    [NonParallelizable]
    internal sealed class PortabilityExportPackageValidationTests
    {
        [Test]
        public async Task ExportAsync_WhenCompleted_ProducesInspectorValidV1Package()
        {
            using var exportRoot = new DisposablePortabilityExportRoot();
            using var extractionRoot = new DisposablePortabilityTestRoot();
            var scenario = new PortabilityExportScenario(
                new PortabilityContractFixture(),
                exportRoot.DestinationPath);

            await new PortabilityExportContractDriver().ExportAsync(scenario).ConfigureAwait(false);
            await using var archive = File.OpenRead(exportRoot.DestinationPath);
            await new PortabilityInspectorDriver()
                .InspectAsync(archive, extractionRoot.ExtractionPath)
                .ConfigureAwait(false);

            File.Exists(Path.Combine(extractionRoot.ExtractionPath, "manifest.json")).Should().BeTrue();
        }

        [Test]
        public async Task ExportAsync_WithIdenticalSourceAndCutoff_ProducesDeterministicArchiveBytes()
        {
            using var firstRoot = new DisposablePortabilityExportRoot();
            using var secondRoot = new DisposablePortabilityExportRoot();
            var fixture = new PortabilityContractFixture();
            var driver = new PortabilityExportContractDriver();

            await driver.ExportAsync(new PortabilityExportScenario(fixture, firstRoot.DestinationPath))
                .ConfigureAwait(false);
            await driver.ExportAsync(new PortabilityExportScenario(fixture, secondRoot.DestinationPath))
                .ConfigureAwait(false);

            Hash(firstRoot.DestinationPath).Should().Equal(Hash(secondRoot.DestinationPath));
        }

        [Test]
        public async Task ExportAsync_WhenTextLooksLikeFormula_EscapesCsvWithoutChangingJson()
        {
            using var exportRoot = new DisposablePortabilityExportRoot();
            var scenario = new PortabilityExportScenario(
                new PortabilityContractFixture(),
                exportRoot.DestinationPath)
            {
                FirstAccountName = "=SUM(1,1)"
            };

            await new PortabilityExportContractDriver().ExportAsync(scenario).ConfigureAwait(false);

            using var archive = new PortabilityExportArchive(exportRoot.DestinationPath);
            archive.ReadJsonLines("accounts.jsonl").First().GetProperty("name").GetString()
                .Should().Be("=SUM(1,1)");
            archive.ReadText("analysis/accounts.csv").Should().Contain("\"'=SUM(1,1)\"");
        }

        private static byte[] Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return SHA256.HashData(stream);
        }
    }
}
