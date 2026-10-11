using System;

namespace HomeBudget.Accounting.Api.Models.DataProtection
{
    public sealed class DataProtectionOperationResponse
    {
        public Guid OperationId { get; set; }
        public string OperationKind { get; set; }
        public string EnvironmentId { get; set; }
        public string LedgerId { get; set; }
        public string LedgerGenerationId { get; set; }
        public string PlanFingerprint { get; set; }
        public string State { get; set; }
        public string Stage { get; set; }
        public bool CancellationRequested { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}
