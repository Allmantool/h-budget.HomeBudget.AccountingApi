using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed record PortabilityImportReconciliationRecord(
        Guid OperationId,
        string ActorId,
        bool Succeeded,
        string EvidenceSha256,
        string ErrorCode,
        DateTime OccurredUtc);
}
