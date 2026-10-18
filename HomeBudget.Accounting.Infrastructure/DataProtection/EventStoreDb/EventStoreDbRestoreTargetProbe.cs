using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbRestoreTargetProbe(
        EventStoreDbNodePathIdentity Identity,
        bool IsProcessRunning,
        bool IsDedicated,
        bool IsEmpty,
        DateTimeOffset ProbedAtUtc);
}
