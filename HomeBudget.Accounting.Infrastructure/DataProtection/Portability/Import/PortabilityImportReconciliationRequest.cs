using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportReconciliationRequest(
        Guid OperationId,
        string ActorId,
        PortabilityImportRequest Import);
}
