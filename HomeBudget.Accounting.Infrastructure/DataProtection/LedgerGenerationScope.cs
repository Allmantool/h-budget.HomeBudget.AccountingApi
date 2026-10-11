namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed record LedgerGenerationScope(
        string LedgerId,
        string LedgerGenerationId,
        long GenerationNumber,
        string GenerationToken)
    {
        public string EnvironmentId { get; init; } = "default";

        public string GenerationTokenValue => GenerationToken.Trim('"');

        public static LedgerGenerationScope Legacy { get; } = new(
            "default",
            "legacy",
            0,
            "\"legacy-token-v1\"");
    }
}
