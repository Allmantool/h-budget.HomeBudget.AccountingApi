using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbBackupFile(
        string SourceNodeId,
        string RelativePath,
        EventStoreDbFileRole Role,
        long SizeBytes,
        string Sha256,
        IReadOnlyCollection<string> Dependencies);
}
