namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public enum EventStoreDbFileRole
    {
        IndexCheckpoint,
        IndexMap,
        IndexTable,
        IndexAuxiliary,
        DatabaseCheckpoint,
        Chunk
    }
}
