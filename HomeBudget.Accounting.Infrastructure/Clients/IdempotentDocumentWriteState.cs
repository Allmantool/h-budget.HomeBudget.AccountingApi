namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public enum IdempotentDocumentWriteState
    {
        Created,
        Existing,
        Conflict
    }
}
