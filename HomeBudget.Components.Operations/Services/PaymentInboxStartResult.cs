namespace HomeBudget.Components.Operations.Services
{
    public sealed record PaymentInboxStartResult
    {
        public string MessageId { get; init; }

        public string Status { get; init; }

        public int RetryCount { get; init; }

        public bool ShouldProcess { get; init; }
    }
}
