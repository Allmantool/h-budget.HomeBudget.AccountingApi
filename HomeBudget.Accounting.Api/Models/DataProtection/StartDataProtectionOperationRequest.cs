using System;
using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Accounting.Api.Models.DataProtection
{
    public sealed class StartDataProtectionOperationRequest
    {
        public Guid PlanId { get; set; }

        [Required]
        public string OperationKind { get; set; }

        [Required]
        public string EnvironmentId { get; set; }

        public string LedgerId { get; set; }
        public string LedgerGenerationId { get; set; }

        [Required]
        [StringLength(64, MinimumLength = 64)]
        public string PlanFingerprint { get; set; }

        [Required]
        public string ConfirmationToken { get; set; }
    }
}
