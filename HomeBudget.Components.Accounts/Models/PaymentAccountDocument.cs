using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Accounts.Models
{
    public class PaymentAccountDocument : DocumentEntity<PaymentAccount>
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
        public long PaymentHistoryProjectionFence { get; init; }
    }
}
