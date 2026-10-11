using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    [TestFixture]
    internal sealed class PortabilityPackageContentSecurityTests : PortabilityPackageSecurityTestBase
    {
        [Test]
        public async Task InspectAsync_WhenRuntimeTypeMetadataIsPresent_RejectsWithoutDeserialization()
        {
            var settings = await File.ReadAllTextAsync(Contract.SamplePath("settings.json"))
                .ConfigureAwait(false);
            var injected = settings.Replace(
                "{",
                "{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\",",
                StringComparison.Ordinal);
            using var archive = Archives.CreateSample(
                new Dictionary<string, byte[]>
                {
                    ["settings.json"] = Encoding.UTF8.GetBytes(injected)
                });

            await AssertRejectedAsync(archive, "archive.runtime-type-metadata").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenManifestChecksumDoesNotMatchEntry_RejectsWithoutOutput()
        {
            var accounts = await File.ReadAllTextAsync(Contract.SamplePath("accounts.jsonl"))
                .ConfigureAwait(false);
            var modified = accounts.Replace(
                "Synthetic BYN account",
                "Changed BYN account",
                StringComparison.Ordinal);
            using var archive = Archives.CreateSample(
                new Dictionary<string, byte[]>
                {
                    ["accounts.jsonl"] = Encoding.UTF8.GetBytes(modified)
                });

            await AssertRejectedAsync(archive, "archive.checksum-mismatch").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenCancellationIsAlreadyRequested_StopsWithoutOutput()
        {
            using var archive = Archives.CreateSample();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            try
            {
                await Inspector.InspectAsync(
                    archive,
                    TestRoot.ExtractionPath,
                    cancellationToken: cancellation.Token).ConfigureAwait(false);
                Assert.Fail("Expected package inspection to observe cancellation.");
            }
            catch (OperationCanceledException)
            {
                // Expected: cancellation must escape rather than become a validation result.
            }
        }

        [Test]
        public async Task InspectAsync_WhenCancellationArrivesDuringArchiveRead_StopsWithoutOutput()
        {
            using var source = Archives.CreateSample();
            using var cancellation = new CancellationTokenSource();
            using var archive = new CancellingReadStream(source.ToArray(), cancellation);

            try
            {
                await Inspector.InspectAsync(
                    archive,
                    TestRoot.ExtractionPath,
                    cancellationToken: cancellation.Token).ConfigureAwait(false);
                Assert.Fail("Expected package inspection to observe cancellation.");
            }
            catch (OperationCanceledException)
            {
                // Expected: cancellation must escape rather than become a validation result.
            }
        }
    }
}
