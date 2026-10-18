using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.DataProtection
{
    public sealed class CreateDataProtectionPlanRequest
    {
        [Required]
        public string OperationKind { get; set; }

        [Required]
        public string EnvironmentId { get; set; }

        public string LedgerId { get; set; }
        public string LedgerGenerationId { get; set; }
        public string ScopeInventoryFingerprint { get; set; }
    }
}
