namespace HomeBudget.Components.Operations.Services
{
    public sealed record PaymentInboxFailureResult
    {
        public string MessageId { get; init; }

        public string Status { get; init; }

        public int RetryCount { get; init; }

        public bool IsPoison => Status == PaymentInboxStatus.Poison;
    }
}
