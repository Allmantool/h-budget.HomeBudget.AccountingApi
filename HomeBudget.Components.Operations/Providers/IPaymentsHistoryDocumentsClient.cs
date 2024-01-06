using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Models;

namespace HomeBudget.Components.Operations.Providers
{
    public interface IPaymentsHistoryDocumentsClient
    {
        Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountingId);

        Task RewriteAllAsync(Guid accountingId, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords);

        Task InsertOneAsync(Guid accountingId, PaymentOperationHistoryRecord payload);

        Task RemoveAsync(Guid accountingId);
    }
}
