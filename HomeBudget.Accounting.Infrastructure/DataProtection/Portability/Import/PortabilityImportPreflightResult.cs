using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportPreflightResult(
        Guid ExportId,
        string PackageSha256,
        string FormatVersion,
        string Completeness,
        long RequiredCapacityBytes,
        IReadOnlyList<PortabilityImportEntityBinding> Entities,
        IReadOnlyList<PortabilityImportRecord> Records = null,
        JsonElement Reconciliation = default);
}
