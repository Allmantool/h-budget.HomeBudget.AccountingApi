using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed record SqlServerPointInTimeRecoveryPlan(
        string DatabaseName,
        DateTimeOffset RequestedCutoffUtc,
        DateTimeOffset ActualCutoffUtc,
        DateTimeOffset StopAtUtc,
        string Precision,
        IReadOnlyCollection<SqlServerBackupMetadata> Backups,
        decimal FirstLsn,
        decimal LastLsn);
}
