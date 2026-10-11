using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed record MongoDbPointInTimeRecoveryPlan(
        string ProviderId,
        string SourceDeploymentId,
        string SnapshotId,
        string ProviderArtifactId,
        DateTimeOffset RequestedCutoffUtc,
        DateTimeOffset ActualCutoffUtc,
        DateTimeOffset SnapshotTimeUtc,
        DateTimeOffset MutationFromUtc,
        DateTimeOffset MutationToUtc,
        string Precision);
}
