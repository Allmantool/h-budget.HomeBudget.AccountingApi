using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class PortabilityImportJournal
    {
        public Guid OperationId { get; init; }

        public Guid ExportId { get; init; }

        public string PackageSha256 { get; init; }

        public string FormatVersion { get; init; }

        public string Completeness { get; init; }

        public string ImportMode { get; init; }

        public string TargetEnvironmentId { get; init; }

        public string TargetLedgerId { get; init; }

        public string TargetGenerationId { get; init; }

        public long TargetGenerationNumber { get; init; }

        public Guid? PreOperationRecoverySetId { get; init; }

        public long EntityCount { get; init; }

        public string State { get; set; }

        public DateTime CreatedUtc { get; init; }

        public DateTime UpdatedUtc { get; set; }

        public IReadOnlyList<PortabilityImportOutcome> Outcomes { get; set; }
    }
}
