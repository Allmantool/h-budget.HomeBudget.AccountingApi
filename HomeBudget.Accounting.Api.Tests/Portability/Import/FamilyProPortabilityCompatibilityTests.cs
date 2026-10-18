using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    [TestFixture]
    internal sealed class FamilyProPortabilityCompatibilityTests
    {
        private DisposablePortabilityImportPackage _package;
        private InMemoryPortabilityImportJournalStore _journal;
        private PortabilityImportPreflightService _preflight;
        private PortabilityImportExecutionRequest _original;

        [SetUp]
        public async Task SetUp()
        {
            _package = new DisposablePortabilityImportPackage();
            _journal = new InMemoryPortabilityImportJournalStore();
            _preflight = new PortabilityImportPreflightService(
                new FamilyProTargetProbe(),
                new PortabilityImportPreflightOptions());
            _original = ExecutionRequest(Guid.NewGuid(), "familypro-generation-1", 1);
            await ExecuteAsync(_original);
        }

        [TearDown]
        public void TearDown() => _package.Dispose();

        [Test]
        public async Task ResumeAsync_OriginalGeneration_PreservesExternalJournalAndLineageIdentities()
        {
            var handoff = await Service().ResumeAsync(
                ResumeRequest("familypro-generation-1", 1),
                _package.RootPath,
                CancellationToken.None);

            handoff.Execution.OperationId.Should().Be(_original.OperationId);
            handoff.ExternalJournalId.Should().Be("familypro-journal-2025-001");
            handoff.SourceBackupReference.Should().Be("backup:familypro12:2025-001");
            handoff.ApprovedManifestReference.Should().Be("manifest:familypro12:approved-001");
            handoff.Lineage.Should().Contain(value =>
                value.SourceRunId == "synthetic-run-001"
                && value.SourceBatchId == "synthetic-batch-002"
                && value.MappingId == "operation-map-100"
                && value.SourceEntityId == "PAYMENT:100");
            handoff.LegacyReferences.Should().Contain(value =>
                value.EntityType == "accounts"
                && value.Reference == "FamilyPro12:ACCOUNT:10");
        }

        [Test]
        public async Task ResumeAsync_AfterResetTargetsNewGeneration_RejectsOldJournal()
        {
            var action = async () => await Service().ResumeAsync(
                ResumeRequest("familypro-generation-2", 2),
                _package.RootPath,
                CancellationToken.None);

            (await action.Should().ThrowAsync<PortabilityImportJournalConflictException>())
                .Which.Code.Should().Be("familypro.resume-generation-conflict");
        }

        [Test]
        public async Task PrepareRemigrationAsync_NewGeneration_CreatesFreshRunAndRetainsSourceMappings()
        {
            var newOperationId = Guid.NewGuid();
            var handoff = await Service().PrepareRemigrationAsync(
                new FamilyProRemigrationRequest(
                    _original.OperationId,
                    newOperationId,
                    "familypro-test-actor",
                    "familypro-journal-2025-001",
                    "familypro-journal-2025-002",
                    "backup:familypro12:2025-001",
                    "manifest:familypro12:approved-001",
                    Import("empty-target", "familypro-generation-2", 2)),
                _package.RootPath,
                CancellationToken.None);
            await ExecuteAsync(handoff.Execution);

            handoff.Execution.OperationId.Should().Be(newOperationId).And.NotBe(_original.OperationId);
            handoff.PriorExternalJournalId.Should().Be("familypro-journal-2025-001");
            handoff.ExternalJournalId.Should().Be("familypro-journal-2025-002");
            handoff.Lineage.Select(value => value.MappingId).Should().Contain("operation-map-100");
            (await _journal.GetAsync(_original.OperationId, CancellationToken.None)).Should().NotBeNull();
            (await _journal.GetAsync(newOperationId, CancellationToken.None)).Should().NotBeNull();
        }

        [Test]
        public async Task PrepareRemigrationAsync_WhenNewRunReusesOldJournalIdentity_RejectsConflict()
        {
            var action = async () => await Service().PrepareRemigrationAsync(
                new FamilyProRemigrationRequest(
                    _original.OperationId,
                    Guid.NewGuid(),
                    "familypro-test-actor",
                    "familypro-journal-2025-001",
                    "familypro-journal-2025-001",
                    "backup:familypro12:2025-001",
                    "manifest:familypro12:approved-001",
                    Import("empty-target", "familypro-generation-2", 2)),
                _package.RootPath,
                CancellationToken.None);

            (await action.Should().ThrowAsync<PortabilityImportJournalConflictException>())
                .Which.Code.Should().Be("familypro.remigration-identity-conflict");
        }

        private FamilyProPortabilityCompatibilityService Service() => new(_preflight, _journal);

        private async Task ExecuteAsync(PortabilityImportExecutionRequest request)
        {
            var execution = new PortabilityImportExecutionService(
                _preflight,
                _journal,
                new RecordingPortabilityImportApplicationAdapter(),
                new PortabilityImportExecutionOptions());
            await execution.ExecuteAsync(request, _package.RootPath, CancellationToken.None);
        }

        private FamilyProResumeRequest ResumeRequest(string generationId, long generationNumber) => new(
            _original.OperationId,
            "familypro-test-actor",
            "familypro-journal-2025-001",
            "backup:familypro12:2025-001",
            "manifest:familypro12:approved-001",
            Import("resume", generationId, generationNumber));

        private PortabilityImportExecutionRequest ExecutionRequest(
            Guid operationId,
            string generationId,
            long generationNumber) => new()
        {
            OperationId = operationId,
            ActorId = "familypro-test-actor",
            Import = Import("empty-target", generationId, generationNumber)
        };

        private PortabilityImportRequest Import(string mode, string generationId, long generationNumber) => new()
        {
            Mode = mode,
            ResumeMode = mode == "resume" ? "empty-target" : null,
            ActorAuthorized = true,
            PackageSha256 = _package.PackageSha256,
            ExpectedPackageSha256 = _package.PackageSha256,
            EnvironmentId = "disposable-familypro-test",
            LedgerId = "default",
            TargetGenerationId = generationId,
            TargetGenerationNumber = generationNumber,
            ExpectedTargetGenerationId = generationId
        };

        private sealed class FamilyProTargetProbe : IPortabilityImportTargetProbe
        {
            public Task<PortabilityImportTargetSnapshot> ProbeAsync(
                PortabilityImportRequest request,
                CancellationToken cancellationToken) => Task.FromResult(new PortabilityImportTargetSnapshot
                {
                    EnvironmentId = request.EnvironmentId,
                    LedgerId = request.LedgerId,
                    GenerationId = request.TargetGenerationId,
                    GenerationNumber = request.TargetGenerationNumber,
                    HasBusinessState = request.Mode == "resume",
                    AvailableBytes = 1_000_000_000
                });
        }
    }
}
