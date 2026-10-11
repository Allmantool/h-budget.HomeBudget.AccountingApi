using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbCopyStep(
        int Order,
        EventStoreDbCopyAction Action,
        string SourcePath,
        string DestinationRelativePath,
        string ExpectedSha256,
        IReadOnlyCollection<string> Dependencies);
}
