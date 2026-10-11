namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    public static class MaintenanceScopeKey
    {
        public static string ForLedger(LedgerGenerationScope generation)
        {
            return DataProtectionPlanBinding.ComputeScopeKey(
                "ledger",
                generation.EnvironmentId,
                generation.LedgerId,
                generation.LedgerGenerationId);
        }
    }
}
