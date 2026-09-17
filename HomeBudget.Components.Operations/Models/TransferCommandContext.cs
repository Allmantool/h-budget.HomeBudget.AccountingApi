namespace HomeBudget.Components.Operations.Models
{
    internal sealed record TransferCommandContext(
        string IdempotencyKeyHash,
        string RequestFingerprint,
        string SourceReference,
        decimal SenderAmount,
        decimal RecipientAmount,
        string SenderCurrency,
        string RecipientCurrency);
}
