namespace HomeBudget.Components.Operations.Services
{
    internal static class PaymentInboxStatus
    {
        public const string Processing = nameof(Processing);

        public const string Processed = nameof(Processed);

        public const string Failed = nameof(Failed);

        public const string Poison = nameof(Poison);

        public const string ReplayRequested = nameof(ReplayRequested);
    }
}
