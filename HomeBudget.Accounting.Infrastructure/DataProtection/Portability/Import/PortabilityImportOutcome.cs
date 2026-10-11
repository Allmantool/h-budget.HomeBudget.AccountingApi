using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class PortabilityImportOutcome
    {
        public Guid ImportOutcomeId { get; init; }

        public Guid OperationId { get; init; }

        public string EntityType { get; init; }

        public string SourceEntityId { get; init; }

        public string TargetEntityId { get; init; }

        public string IdempotencyKey { get; init; }

        public string Fingerprint { get; init; }

        public string State { get; set; }

        public string ErrorClassification { get; set; }

        public DateTime CreatedUtc { get; init; }

        public DateTime UpdatedUtc { get; set; }
    }
}
