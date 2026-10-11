using System;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    [TestFixture]
    internal sealed class PortabilityPackagePathSecurityTests : PortabilityPackageSecurityTestBase
    {
        [TestCase("../escape-probe.txt")]
        [TestCase("..\\escape-probe.txt")]
        [TestCase("%2e%2e%2fescape-probe.txt")]
        [TestCase("%2e%2e%5cescape-probe.txt")]
        public async Task InspectAsync_WhenEntryTraversesOrEncodesTraversal_RejectsBeforeExtraction(
            string entryPath)
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, entryPath, "escape"));

            await AssertRejectedAsync(archive, "archive.path-traversal").ConfigureAwait(false);
        }

        [TestCase("/absolute-probe.txt")]
        [TestCase("\\absolute-probe.txt")]
        [TestCase("\\\\home-ledger-invalid\\share\\absolute-probe.txt")]
        public async Task InspectAsync_WhenEntryPathIsRootedOrUnc_RejectsBeforeExtraction(string entryPath)
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, entryPath, "absolute"));

            await AssertRejectedAsync(archive, "archive.absolute-path").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryPathContainsDriveRoot_RejectsBeforeExtraction()
        {
            var target = System.IO.Path.Combine(TestRoot.RootPath, "absolute-probe.txt");
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, target, "absolute"));

            await AssertRejectedAsync(archive, "archive.absolute-path").ConfigureAwait(false);
        }

        [TestCase("C:drive-relative-probe.txt")]
        [TestCase("accounts.jsonl:alternate-stream")]
        public async Task InspectAsync_WhenEntryPathContainsAlternateStreamSyntax_RejectsBeforeExtraction(
            string entryPath)
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, entryPath, "stream"));

            await AssertRejectedAsync(archive, "archive.alternate-stream").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryIsUnixSymbolicLink_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(addEntries: value =>
            {
                var link = PortabilityArchiveFactory.AddText(value, "account-link.jsonl", "accounts.jsonl");
                link.ExternalAttributes = unchecked((int)0xA1FF0000);
            });

            await AssertRejectedAsync(archive, "archive.unsafe-link").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryNameIsDuplicated_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, "accounts.jsonl", "{}"));

            await AssertRejectedAsync(archive, "archive.duplicate-entry").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryNamesDifferOnlyByCase_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, "ACCOUNTS.JSONL", "{}"));

            await AssertRejectedAsync(archive, "archive.case-collision").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenNestedArchiveExtensionIsPresent_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, "nested.zip", "PK"));

            await AssertRejectedAsync(archive, "archive.nested-archive").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenAllowlistedCsvContainsArchiveSignature_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(addEntries: value =>
                PortabilityArchiveFactory.AddBytes(
                    value,
                    "analysis/accounts.csv",
                    [0x50, 0x4b, 0x03, 0x04, 0x00],
                    CompressionLevel.NoCompression));

            await AssertRejectedAsync(archive, "archive.nested-archive").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryIsNotInCatalog_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(
                addEntries: value => PortabilityArchiveFactory.AddText(value, "unexpected.json", "{}"));

            await AssertRejectedAsync(archive, "archive.unsupported-entry").ConfigureAwait(false);
        }

        [Test]
        public async Task InspectAsync_WhenEntryTypeIsExecutable_RejectsBeforeExtraction()
        {
            using var archive = Archives.CreateSample(addEntries: value =>
                PortabilityArchiveFactory.AddBytes(
                    value,
                    "analysis/accounts.exe",
                    Encoding.ASCII.GetBytes("MZ"),
                    CompressionLevel.NoCompression));

            await AssertRejectedAsync(archive, "archive.unsupported-entry-type").ConfigureAwait(false);
        }
    }
}
