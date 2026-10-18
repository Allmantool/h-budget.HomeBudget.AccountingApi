using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Artifacts
{
    [TestFixture]
    internal sealed class RecoveryArtifactPipelineTests
    {
        [Test]
        public async Task Pipeline_WithProtectedIndependentEvidence_PublishesManifestLastAndCleansPlaintext()
        {
            using var fixture = new Fixture();

            var capture = await fixture.Pipeline.CaptureAsync(fixture.Draft, CancellationToken.None);
            File.Exists(fixture.Source.LastPlaintextPath).Should().BeFalse();
            fixture.ManifestExists().Should().BeFalse();

            var copy = await fixture.Pipeline.CopyAsync(fixture.Draft, capture, CancellationToken.None);
            fixture.ManifestExists().Should().BeFalse();
            var verification = await fixture.Pipeline.VerifyAsync(
                fixture.Draft,
                capture,
                copy,
                CancellationToken.None);

            verification.Succeeded.Should().BeTrue();
            fixture.ManifestExists().Should().BeTrue();
            fixture.Encryptor.LargestRead.Should().BeLessThanOrEqualTo(64 * 1024);
            fixture.ReadAllProtectedBytes().AsSpan().IndexOf(fixture.SecretBytes).Should().Be(-1);
            fixture.ReadManifest().Should().Contain(fixture.Draft.KeyReference);
            fixture.ReadManifest().Should().NotContain(Convert.ToBase64String(fixture.SecretBytes));
        }

        [Test]
        public void FileSystemAdapter_WithoutMatchingMarker_IsRejected()
        {
            using var root = new DisposableArtifactTestRoot("invalid-marker");
            var marker = Path.Combine(root.RootPath, DisposableFileSystemArtifactStore.MarkerFileName);
            File.WriteAllText(marker, "different-root\ndifferent-token");

            var create = () => new DisposableFileSystemArtifactStore(
                root.Identity,
                ArtifactProtectionProfile.DisposableTest,
                Fixture.Capability("store", "host-b", "disk-b"));

            create.Should().Throw<ArtifactProtectionException>()
                .Which.Code.Should().Be("disposable-root-unverified");
            File.WriteAllText(marker, $"{root.Identity.RootId}\n{root.Identity.OwnershipToken}");
        }

        [Test]
        public void FileSystemAdapter_InProductionProfile_IsRejected()
        {
            using var root = new DisposableArtifactTestRoot("production-rejected");

            var create = () => new DisposableFileSystemArtifactStore(
                root.Identity,
                ArtifactProtectionProfile.Production,
                Fixture.Capability("store", "host-b", "disk-b"));

            create.Should().Throw<ArtifactProtectionException>()
                .Which.Code.Should().Be("filesystem-production-forbidden");
        }

        [Test]
        public async Task Capture_WhenKeyIsNotSeparatelyRecoverable_FailsBeforePublication()
        {
            using var fixture = new Fixture();
            fixture.Encryptor.IsSeparatelyRecoverable = false;

            var act = () => fixture.Pipeline.CaptureAsync(fixture.Draft, CancellationToken.None);

            await act.Should().ThrowAsync<ArtifactProtectionException>()
                .Where(exception => exception.Code == "key-not-separately-recoverable");
            fixture.ManifestExists().Should().BeFalse();
        }

        [Test]
        public async Task Copy_WhenStoreSharesSourceFailureDomain_FailsBeforePublication()
        {
            using var fixture = new Fixture(storeHost: "source-host", storeFailureDomain: "source-disk");
            var capture = await fixture.Pipeline.CaptureAsync(fixture.Draft, CancellationToken.None);

            var act = () => fixture.Pipeline.CopyAsync(fixture.Draft, capture, CancellationToken.None);

            await act.Should().ThrowAsync<ArtifactProtectionException>()
                .Where(exception => exception.Code == "store-not-independent");
            fixture.ManifestExists().Should().BeFalse();
        }

        [Test]
        public async Task Copy_WhenProviderFails_QuarantinesWithoutPublishingOrLeakingError()
        {
            using var fixture = new Fixture(copyFails: true);
            var capture = await fixture.Pipeline.CaptureAsync(fixture.Draft, CancellationToken.None);

            var act = () => fixture.Pipeline.CopyAsync(fixture.Draft, capture, CancellationToken.None);

            var failure = await act.Should().ThrowAsync<ArtifactProtectionException>();
            failure.Which.Code.Should().Be("independent-copy-failed");
            failure.Which.Message.Should().NotContain("credential canary");
            fixture.ManifestExists().Should().BeFalse();
            fixture.QuarantineExists().Should().BeTrue();
            File.Exists(fixture.Source.LastPlaintextPath).Should().BeFalse();
        }

        [Test]
        public async Task Capture_WhenSourceFails_CleansPlaintextAndDoesNotPublish()
        {
            using var fixture = new Fixture(sourceFails: true);

            var act = () => fixture.Pipeline.CaptureAsync(fixture.Draft, CancellationToken.None);

            var failure = await act.Should().ThrowAsync<ArtifactProtectionException>();
            failure.Which.Code.Should().Be("capture-source-failed");
            failure.Which.Message.Should().NotContain("canary secret");
            Directory.Exists(fixture.PlaintextWorkspace()).Should().BeFalse();
            fixture.ManifestExists().Should().BeFalse();
        }

        [Test]
        public async Task Verify_WhenIndependentObjectIsCorrupt_QuarantinesAndDoesNotPublish()
        {
            using var fixture = new Fixture();
            var capture = await fixture.Pipeline.CaptureAsync(fixture.Draft, CancellationToken.None);
            var copy = await fixture.Pipeline.CopyAsync(fixture.Draft, capture, CancellationToken.None);
            fixture.CorruptIndependentObject();

            var act = () => fixture.Pipeline.VerifyAsync(fixture.Draft, capture, copy, CancellationToken.None);

            await act.Should().ThrowAsync<ArtifactProtectionException>()
                .Where(exception => exception.Code == "independent-verification-failed");
            fixture.ManifestExists().Should().BeFalse();
            fixture.QuarantineExists().Should().BeTrue();
        }

        [Test]
        public async Task Capture_WithCancellation_PropagatesAndCleansPlaintext()
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var act = () => fixture.Pipeline.CaptureAsync(fixture.Draft, cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            Directory.Exists(fixture.PlaintextWorkspace()).Should().BeFalse();
            fixture.ManifestExists().Should().BeFalse();
        }

        private sealed class Fixture : IDisposable
        {
            private readonly DisposableArtifactTestRoot _stagingRoot = new("staging");
            private readonly DisposableArtifactTestRoot _independentRoot = new("independent");

            public Fixture(
                bool sourceFails = false,
                bool copyFails = false,
                string storeHost = "independent-host",
                string storeFailureDomain = "independent-disk")
            {
                SecretBytes = Encoding.UTF8.GetBytes("plain financial capture canary");
                Source = new TestRecoveryArtifactSource(SecretBytes, sourceFails);
                Encryptor = new TestStreamingArtifactEncryptor();
                var staging = new DisposableFileSystemArtifactStore(
                    _stagingRoot.Identity,
                    ArtifactProtectionProfile.DisposableTest,
                    Capability("staging", "source-host", "source-disk"));
                var independentFileSystem = new DisposableFileSystemArtifactStore(
                    _independentRoot.Identity,
                    ArtifactProtectionProfile.DisposableTest,
                    Capability("independent", storeHost, storeFailureDomain));
                IIndependentRecoveryArtifactStore independent = copyFails
                    ? new FailingIndependentArtifactStore(independentFileSystem)
                    : independentFileSystem;
                Draft = CreateDraft();
                Pipeline = new RecoveryArtifactPipeline(
                    ArtifactProtectionPolicy.Disposable("test", "source-host", "source-disk"),
                    new[] { Source },
                    Encryptor,
                    staging,
                    independent);
            }

            public byte[] SecretBytes { get; }

            public TestRecoveryArtifactSource Source { get; }

            public TestStreamingArtifactEncryptor Encryptor { get; }

            public RecoveryManifestDraft Draft { get; }

            public RecoveryArtifactPipeline Pipeline { get; }

            public static ArtifactStoreCapability Capability(string storeId, string host, string failureDomain) => new(
                "disposable-filesystem",
                "1",
                storeId,
                host,
                failureDomain,
                ArtifactTransportSecurity.LocalProcessOnly,
                ArtifactImmutabilityState.Immutable,
                true,
                true);

            public bool ManifestExists() => File.Exists(
                Path.Combine(_independentRoot.RootPath, "manifests", $"{Draft.RecoverySetId:N}.json"));

            public string ReadManifest() => File.ReadAllText(
                Path.Combine(_independentRoot.RootPath, "manifests", $"{Draft.RecoverySetId:N}.json"));

            public byte[] ReadAllProtectedBytes()
            {
                var file = Directory.EnumerateFiles(
                    Path.Combine(_independentRoot.RootPath, "objects"),
                    "*",
                    SearchOption.AllDirectories).Single();
                return File.ReadAllBytes(file);
            }

            public void CorruptIndependentObject()
            {
                var file = Directory.EnumerateFiles(
                    Path.Combine(_independentRoot.RootPath, "objects"),
                    "*",
                    SearchOption.AllDirectories).Single();
                File.SetAttributes(file, FileAttributes.Normal);
                File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            }

            public string PlaintextWorkspace() => Path.Combine(
                _stagingRoot.RootPath,
                "plaintext",
                Draft.RecoverySetId.ToString("N"));

            public bool QuarantineExists() => Directory.Exists(
                Path.Combine(_independentRoot.RootPath, "quarantine", Draft.RecoverySetId.ToString("N")));

            public void Dispose()
            {
                Encryptor.Dispose();
                _independentRoot.Dispose();
                _stagingRoot.Dispose();
            }

            private static RecoveryManifestDraft CreateDraft()
            {
                var id = Guid.NewGuid();
                return new RecoveryManifestDraft(
                    id,
                    Guid.NewGuid(),
                    "test",
                    "ledger:generation",
                    "generation",
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow,
                    "kms://test/key/42@7",
                    "draft-checksum",
                    "{\"schemaVersion\":\"home-ledger-recovery-set/v1\"}",
                    Array.Empty<CaptureComponentReceipt>(),
                    new PendingWorkDisposition("drained", 0, 0, 0, "{}"));
            }
        }
    }
}
