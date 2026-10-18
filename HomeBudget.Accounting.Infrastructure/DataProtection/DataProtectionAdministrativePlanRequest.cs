namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class DataProtectionAdministrativePlanRequest
    {
        public string OperationKind { get; init; }
        public string EnvironmentId { get; init; }
        public string LedgerId { get; init; }
        public string LedgerGenerationId { get; init; }
        public string ScopeInventoryFingerprint { get; init; }
        public string ActorId { get; init; }
    }
}
