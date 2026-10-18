using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed record MongoDbTopologyEvidence(
        string ServerVersion,
        MongoDbDeploymentTopology Topology,
        string DeploymentId,
        string ReplicaSetName,
        DateTimeOffset ProbedAtUtc);
}
