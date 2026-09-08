using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Operations.Models
{
    public sealed class PaymentHistoryDocument : DocumentEntity<PaymentOperationHistoryRecord>
    {
        public System.Guid ProjectionRunId { get; init; }
    }
}
