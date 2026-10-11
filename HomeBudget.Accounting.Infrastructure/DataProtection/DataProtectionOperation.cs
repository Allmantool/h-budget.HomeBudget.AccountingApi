using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class DataProtectionOperation
    {
        public Guid OperationId { get; init; }
        public string OperationKind { get; init; }
        public string ScopeKind { get; init; }
        public string ScopeKey { get; init; }
        public string EnvironmentId { get; init; }
        public string LedgerId { get; init; }
        public string GenerationId { get; init; }
        public string PlanFingerprint { get; init; }
        public string StateValue { get; init; }
        public string Stage { get; init; }
        public bool CancellationRequested { get; init; }
        public DateTime CreatedUtc { get; init; }
        public DateTime UpdatedUtc { get; init; }
        internal byte[] RowVersion { get; init; }

        public DataProtectionOperationState State => DataProtectionOperationStateNames.Parse(StateValue);
    }
}
