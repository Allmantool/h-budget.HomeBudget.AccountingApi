using System.Text.Json;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class PortabilityImportScenario
    {
        public PortabilityImportScenario(DisposablePortabilityImportPackage package)
        {
            Package = package;
        }

        public DisposablePortabilityImportPackage Package { get; }

        public string Mode { get; set; } = "validate-only";

        public bool Authorized { get; set; } = true;

        public bool HasBusinessState { get; set; }

        public bool IsActive { get; set; }

        public long AvailableBytes { get; set; } = 1_000_000_000;

        public string ExpectedPackageSha256 { get; set; }

        public string ExpectedTargetGenerationId { get; set; }

        public bool MaintenanceIsolationConfirmed { get; set; }

        public string PreOperationRecoverySetId { get; set; }

        public int ProbeCalls { get; set; }

        public int TargetMutationAttempts { get; set; }

        public string RequestJson => JsonSerializer.Serialize(new
        {
            mode = Mode,
            resumeMode = Mode == "resume" ? "empty-target" : null,
            actorAuthorized = Authorized,
            packageSha256 = Package.PackageSha256,
            expectedPackageSha256 = ExpectedPackageSha256 ?? Package.PackageSha256,
            environmentId = "disposable-unit-test",
            ledgerId = "default",
            targetGenerationId = "legacy",
            targetGenerationNumber = 0,
            expectedTargetGenerationId = ExpectedTargetGenerationId ?? "legacy",
            maintenanceIsolationConfirmed = MaintenanceIsolationConfirmed,
            preOperationRecoverySetId = PreOperationRecoverySetId
        });

        public string TargetJson => JsonSerializer.Serialize(new
        {
            environmentId = "disposable-unit-test",
            ledgerId = "default",
            generationId = "legacy",
            generationNumber = 0,
            hasBusinessState = HasBusinessState,
            isActive = IsActive,
            availableBytes = AvailableBytes
        });
    }
}
