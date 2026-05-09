namespace HomeBudget.Components.Operations.Options
{
    public sealed record PaymentInboxOptions
    {
        public const string SectionName = nameof(PaymentInboxOptions);

        public int MaxRetryAttempts { get; init; } = 5;
    }
}
