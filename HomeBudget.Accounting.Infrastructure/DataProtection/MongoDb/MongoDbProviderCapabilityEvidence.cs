using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed record MongoDbProviderCapabilityEvidence(
        string ProviderId,
        string ProviderVersion,
        string SourceDeploymentId,
        MongoDbProtectionMechanism Mechanism,
        bool SupportsPointInTimeRecovery,
        bool UsesImmutableArtifacts,
        DateTimeOffset RecoverableFromUtc,
        DateTimeOffset RecoverableToUtc,
        IReadOnlyCollection<MongoDbRecoveryGap> Gaps,
        DateTimeOffset ProbedAtUtc);
}
