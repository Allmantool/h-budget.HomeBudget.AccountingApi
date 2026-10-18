namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportExecutionResult(
        string State,
        int AcceptedCount,
        int SettledCount,
        int RemainingCount);
}
