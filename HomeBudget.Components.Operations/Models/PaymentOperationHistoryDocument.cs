using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Components.Operations.Models
{
    public class PaymentHistoryDocument : DocumentEntity<PaymentOperationHistoryRecord>
    {
        public System.Guid ProjectionRunId { get; init; }
    }
}
