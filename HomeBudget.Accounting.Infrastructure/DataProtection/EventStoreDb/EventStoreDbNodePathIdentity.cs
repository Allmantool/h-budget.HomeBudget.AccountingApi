namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed record EventStoreDbNodePathIdentity(
        string NodeId,
        string DatabasePath,
        string IndexPath);
}
