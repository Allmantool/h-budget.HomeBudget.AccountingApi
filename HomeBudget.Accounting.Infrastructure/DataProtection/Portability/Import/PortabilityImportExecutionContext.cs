namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportExecutionContext(
        string EnvironmentId,
        string LedgerId,
        string GenerationId,
        long GenerationNumber,
        string Mode);
}
