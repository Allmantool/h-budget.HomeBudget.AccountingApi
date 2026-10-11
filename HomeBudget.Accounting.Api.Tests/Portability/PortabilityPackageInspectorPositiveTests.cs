using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    [TestFixture]
    internal sealed class PortabilityPackageInspectorPositiveTests : PortabilityPackageSecurityTestBase
    {
        [Test]
        public async Task InspectAsync_WhenPackageIsValid_PromotesExactPackageBytes()
        {
            using var archive = Archives.CreateSample();
            try
            {
                await Inspector.InspectAsync(archive, TestRoot.ExtractionPath).ConfigureAwait(false);

                var extracted = Directory
                    .EnumerateFiles(TestRoot.ExtractionPath, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(TestRoot.ExtractionPath, path).Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal);
                extracted.Should().Equal(Contract.SampleEntries());
                foreach (var path in extracted)
                {
                    PortabilityContractFixture.Sha256(Path.Combine(TestRoot.ExtractionPath, path))
                        .Should().Be(PortabilityContractFixture.Sha256(Contract.SamplePath(path)));
                }
            }
            finally
            {
                TestRoot.ResetExtraction();
            }
        }

        [Test]
        public async Task InspectAsync_WhenTargetIsNotEmpty_RejectsWithoutRemovingCallerFile()
        {
            var sentinel = Path.Combine(TestRoot.ExtractionPath, "caller-owned.txt");
            await File.WriteAllTextAsync(sentinel, "preserve").ConfigureAwait(false);
            using var archive = Archives.CreateSample();
            try
            {
                var code = await Inspector
                    .InspectRejectedAsync(archive, TestRoot.ExtractionPath)
                    .ConfigureAwait(false);

                code.Should().Be("archive.target-not-empty");
                (await File.ReadAllTextAsync(sentinel).ConfigureAwait(false)).Should().Be("preserve");
            }
            finally
            {
                File.Delete(sentinel);
            }
        }

        [Test]
        public async Task InspectAsync_WhenDocumentedAnalysisCsvIsPresent_AcceptsItsManifestMediaType()
        {
            const string path = "analysis/accounts.csv";
            var csv = Encoding.UTF8.GetBytes("accountId,name\r\n40000000-0000-4000-8000-000000000001,Example\r\n");
            var manifest = Contract.ReadSampleObject("manifest.json");
            manifest["files"] !.AsArray().Add(
                new JsonObject
                {
                    ["path"] = path,
                    ["mediaType"] = "text/csv",
                    ["byteSize"] = csv.LongLength,
                    ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(csv))
                });
            using var archive = Archives.CreateSample(
                new Dictionary<string, byte[]>
                {
                    ["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString())
                },
                value => PortabilityArchiveFactory.AddBytes(value, path, csv));

            try
            {
                await Inspector.InspectAsync(archive, TestRoot.ExtractionPath).ConfigureAwait(false);

                (await File.ReadAllBytesAsync(
                    Path.Combine(TestRoot.ExtractionPath, "analysis", "accounts.csv")).ConfigureAwait(false))
                    .Should().Equal(csv);
            }
            finally
            {
                TestRoot.ResetExtraction();
            }
        }
    }
}
