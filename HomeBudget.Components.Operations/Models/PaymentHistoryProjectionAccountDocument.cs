using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Operations.Models
{
    public sealed class PaymentHistoryProjectionAccountDocument : DocumentEntity<PaymentHistoryProjectionAccount>
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
    }
}
