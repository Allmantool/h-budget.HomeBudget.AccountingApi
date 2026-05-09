namespace HomeBudget.Components.Operations.Options
{
    public sealed record PaymentOutboxPublisherOptions
    {
        public const string SectionName = nameof(PaymentOutboxPublisherOptions);

        public bool IsEnabled { get; init; } = true;

        public int BatchSize { get; init; } = 25;

        public int PollIntervalMilliseconds { get; init; } = 1000;

        public int LockTimeoutSeconds { get; init; } = 60;

        public int MaxRetryAttempts { get; init; } = 5;
    }
}
