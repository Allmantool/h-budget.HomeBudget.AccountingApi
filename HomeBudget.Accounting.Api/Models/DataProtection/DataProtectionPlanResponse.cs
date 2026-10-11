using System;

namespace HomeBudget.Accounting.Api.Models.DataProtection
{
    public sealed class DataProtectionPlanResponse
    {
        public Guid PlanId { get; set; }
        public string OperationKind { get; set; }
        public string EnvironmentId { get; set; }
        public string LedgerId { get; set; }
        public string LedgerGenerationId { get; set; }
        public string ScopeInventoryFingerprint { get; set; }
        public string PlanFingerprint { get; set; }
        public string ConfirmationToken { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }
}
