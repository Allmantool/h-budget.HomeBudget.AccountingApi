namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal sealed class DataProtectionAuthorizationTarget
    {
        public DataProtectionAuthorizationTarget(
            string environmentId,
            string ledgerId,
            string ledgerGenerationId)
        {
            EnvironmentId = environmentId;
            LedgerId = ledgerId;
            LedgerGenerationId = ledgerGenerationId;
        }

        public string EnvironmentId { get; }
        public string LedgerId { get; }
        public string LedgerGenerationId { get; }

        public DataProtectionAuthorizationTarget WithLedgerId(string ledgerId)
        {
            return new DataProtectionAuthorizationTarget(EnvironmentId, ledgerId, LedgerGenerationId);
        }

        public DataProtectionAuthorizationTarget WithLedgerGenerationId(string ledgerGenerationId)
        {
            return new DataProtectionAuthorizationTarget(EnvironmentId, LedgerId, ledgerGenerationId);
        }
    }
}
