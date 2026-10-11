using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed record MongoDbSnapshotEvidence(
        string ProviderId,
        string SourceDeploymentId,
        string SnapshotId,
        string ProviderArtifactId,
        DateTimeOffset SnapshotTimeUtc,
        DateTimeOffset MutationFromUtc,
        DateTimeOffset MutationToUtc,
        DateTimeOffset RecoverableFromUtc,
        DateTimeOffset RecoverableToUtc,
        IReadOnlyCollection<MongoDbRecoveryGap> Gaps,
        bool IsImmutable,
        bool IsVerified);
}
