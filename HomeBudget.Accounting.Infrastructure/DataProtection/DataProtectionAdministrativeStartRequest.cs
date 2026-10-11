using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class DataProtectionAdministrativeStartRequest
    {
        public Guid PlanId { get; init; }
        public string OperationKind { get; init; }
        public string EnvironmentId { get; init; }
        public string LedgerId { get; init; }
        public string LedgerGenerationId { get; init; }
        public string PlanFingerprint { get; init; }
        public string ConfirmationToken { get; init; }
        public string ActorId { get; init; }
    }
}
