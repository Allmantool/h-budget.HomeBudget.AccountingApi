using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbSourceProbe(
        EventStoreDbNodePathIdentity Identity,
        string ServerVersion,
        bool IsClusterConnected,
        bool IsUpToDate,
        bool IsScavengeRunning,
        long WriterPosition,
        long ChaserPosition,
        IReadOnlyCollection<string> ExpectedIndexFiles,
        DateTimeOffset ProbedAtUtc);
}
