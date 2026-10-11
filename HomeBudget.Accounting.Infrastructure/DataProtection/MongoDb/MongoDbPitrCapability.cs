using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed record MongoDbPitrCapability(
        MongoDbPitrCapabilityState State,
        string ReasonCode,
        string ServerVersion,
        MongoDbDeploymentTopology Topology,
        string DeploymentId,
        string ProviderId,
        DateTimeOffset? RecoverableFromUtc,
        DateTimeOffset? RecoverableToUtc,
        IReadOnlyCollection<MongoDbRecoveryGap> Gaps);
}
