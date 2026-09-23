namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public sealed record IdempotentDocumentWriteContext(
        string IdempotencyKeyHash,
        string RequestFingerprint,
        string SourceReference);
}
