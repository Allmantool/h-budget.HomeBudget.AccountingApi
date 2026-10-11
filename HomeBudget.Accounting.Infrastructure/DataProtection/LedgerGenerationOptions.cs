namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class LedgerGenerationOptions
    {
        public const string SectionName = "LedgerGeneration";

        public string EnvironmentId { get; init; } = "default";

        public string LedgerId { get; init; } = "default";
    }
}
