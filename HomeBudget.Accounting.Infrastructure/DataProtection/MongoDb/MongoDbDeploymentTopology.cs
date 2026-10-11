namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public enum MongoDbDeploymentTopology
    {
        Standalone,
        ReplicaSet,
        ShardedCluster,
        Unknown
    }
}
