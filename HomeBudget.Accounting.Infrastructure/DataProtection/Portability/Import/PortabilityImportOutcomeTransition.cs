using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed record PortabilityImportOutcomeTransition(
        Guid OperationId,
        string EntityType,
        string SourceEntityId,
        string Fingerprint,
        string IdempotencyKey,
        string ExpectedState,
        string NextState,
        string ErrorClassification,
        DateTime UpdatedUtc);
}
