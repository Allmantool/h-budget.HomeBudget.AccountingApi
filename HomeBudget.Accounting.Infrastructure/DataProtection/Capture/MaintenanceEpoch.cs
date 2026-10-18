using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed record MaintenanceEpoch(
        Guid EpochId,
        Guid OperationId,
        string ScopeKey,
        string GenerationId,
        long EpochNumber,
        DateTimeOffset AdmissionClosedUtc);
}
