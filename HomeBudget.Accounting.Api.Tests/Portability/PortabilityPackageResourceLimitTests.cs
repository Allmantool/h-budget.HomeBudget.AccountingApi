using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    [TestFixture]
    internal sealed class PortabilityPackageResourceLimitTests : PortabilityPackageSecurityTestBase
    {
        [Test]
        public async Task InspectAsync_WhenEntryCountLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxEntryCount",
                1,
                "archive.entry-count-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenArchiveCompressedSizeLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxArchiveCompressedBytes",
                archive.Length - 1,
                "archive.compressed-size-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryCompressedSizeLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxEntryCompressedBytes",
                1L,
                "archive.entry-compressed-size-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenTotalUncompressedSizeLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxTotalUncompressedBytes",
                1L,
                "archive.uncompressed-size-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryUncompressedSizeLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxEntryUncompressedBytes",
                1L,
                "archive.entry-uncompressed-size-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenExpansionRatioLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxExpansionRatio",
                1.1,
                "archive.expansion-ratio-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenJsonLineLengthLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxJsonLineBytes",
                64,
                "archive.line-length-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenJsonlRecordCountLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxJsonlRecordCount",
                1,
                "archive.record-count-limit").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenJsonDepthLimitIsExceeded_RejectsWithinBound()
        {
            using var archive = Archives.CreateSample();

            await AssertLimitAsync(
                archive,
                "MaxJsonDepth",
                1,
                "archive.json-depth-limit").ConfigureAwait(false);
        }

        private async Task AssertLimitAsync(
            MemoryStream archive,
            string option,
            object value,
            string expectedCode)
        {
            var code = await Inspector.InspectRejectedAsync(
                archive,
                TestRoot.ExtractionPath,
                new Dictionary<string, object> { [option] = value }).ConfigureAwait(false);

            code.Should().Be(expectedCode);
        }
    }
}
