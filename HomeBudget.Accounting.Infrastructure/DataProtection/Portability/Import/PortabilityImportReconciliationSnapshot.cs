using System.Collections.Generic;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportReconciliationSnapshot(
        IReadOnlyList<PortabilityImportRecord> Records,
        JsonElement Summary);
}
