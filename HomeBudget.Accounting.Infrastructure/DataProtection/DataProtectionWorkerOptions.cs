namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class DataProtectionWorkerOptions
    {
        public const string SectionName = "DataProtectionWorkerOptions";

        public bool IsEnabled { get; init; }
        public int PollIntervalMilliseconds { get; init; } = 1000;
        public int LeaseDurationSeconds { get; init; } = 60;
    }
}
