namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public enum MongoDbProtectionMechanism
    {
        ContinuousBackup,
        LogicalDump,
        FilesystemSnapshot
    }
}
