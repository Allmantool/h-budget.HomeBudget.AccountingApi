using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed record SqlServerCapturePlan(
        Guid PlanId,
        string DatabaseName,
        SqlServerBackupKind Kind,
        string BackupTarget,
        string ServerName,
        string ProductVersion,
        Guid DatabaseId,
        DateTimeOffset PlannedAtUtc);
}
