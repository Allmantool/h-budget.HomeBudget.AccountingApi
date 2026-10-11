using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public sealed record LedgerGenerationResponse
    {
        public string LedgerId { get; init; }
        public string LedgerGenerationId { get; init; }
        public string GenerationToken { get; init; }

        public static LedgerGenerationResponse From(LedgerGenerationScope generation)
        {
            return new LedgerGenerationResponse
            {
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };
        }
    }
}
