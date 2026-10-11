using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class DataProtectionOperationRequest
    {
        public Guid OperationId { get; init; }
        public string OperationKind { get; init; }
        public string ScopeKind { get; init; }
        public string ScopeKey { get; init; }
        public string EnvironmentId { get; init; }
        public string LedgerId { get; init; }
        public string GenerationId { get; init; }
        public string PlanFingerprint { get; init; }
        public string ActorId { get; init; }
        public DateTime CreatedUtc { get; init; }
    }
}
