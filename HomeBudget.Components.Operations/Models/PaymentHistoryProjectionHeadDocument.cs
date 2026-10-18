using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Operations.Models
{
    public sealed class PaymentHistoryProjectionHeadDocument : DocumentEntity<PaymentHistoryProjectionHead>
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
    }
}
