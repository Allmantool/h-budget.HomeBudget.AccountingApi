using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class PortabilityImportJournalRequest
    {
        public Guid OperationId { get; init; }

        public string ActorId { get; init; }

        public string Mode { get; init; }

        public string TargetEnvironmentId { get; init; }

        public string TargetLedgerId { get; init; }

        public string TargetGenerationId { get; init; }

        public long TargetGenerationNumber { get; init; }

        public Guid? PreOperationRecoverySetId { get; init; }

        public DateTime CreatedUtc { get; init; }

        public PortabilityImportPreflightResult Preflight { get; init; }
    }
}
