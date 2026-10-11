using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed record SqlServerProtectionProbe(
        string ServerName,
        string ProductVersion,
        int ProductMajorVersion,
        string ProductLevel,
        string Edition,
        int EngineEdition,
        string DatabaseName,
        Guid DatabaseId,
        string RecoveryModel,
        string DatabaseState,
        DateTimeOffset ProbedAtUtc,
        IReadOnlyCollection<string> Blockers)
    {
        public bool IsCompatible => Blockers.Count == 0;
    }
}
