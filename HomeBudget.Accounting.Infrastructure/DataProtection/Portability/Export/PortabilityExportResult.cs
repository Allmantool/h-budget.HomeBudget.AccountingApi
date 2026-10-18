using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed record PortabilityExportResult(
        Guid ExportId,
        string DestinationPath,
        DateTimeOffset CommonCutoffUtc,
        long OperationCount);
}
