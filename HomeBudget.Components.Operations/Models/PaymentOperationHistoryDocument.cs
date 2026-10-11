using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Operations.Models
{
    public sealed class PaymentHistoryDocument : DocumentEntity<PaymentOperationHistoryRecord>
    {
        public string LedgerId { get; init; } = "default";
        public string LedgerGenerationId { get; init; } = "legacy";
        public System.Guid ProjectionRunId { get; init; }
        public string GenerationId { get; init; }
    }
}
