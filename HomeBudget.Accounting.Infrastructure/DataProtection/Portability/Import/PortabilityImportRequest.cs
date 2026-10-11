using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportRequest
    {
        public string Mode { get; set; }

        public string ResumeMode { get; set; }

        public bool ActorAuthorized { get; set; }

        public string PackageSha256 { get; set; }

        public string ExpectedPackageSha256 { get; set; }

        public string EnvironmentId { get; set; }

        public string LedgerId { get; set; }

        public string TargetGenerationId { get; set; }

        public long TargetGenerationNumber { get; set; }

        public string ExpectedTargetGenerationId { get; set; }

        public bool MaintenanceIsolationConfirmed { get; set; }

        public string PreOperationRecoverySetId { get; set; }
    }
}
