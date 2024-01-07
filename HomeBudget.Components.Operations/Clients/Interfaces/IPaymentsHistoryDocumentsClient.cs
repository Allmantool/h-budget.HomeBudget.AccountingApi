using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Clients.Interfaces
{
    public interface IPaymentsHistoryDocumentsClient : IDocumentClient<PaymentHistoryDocument>
    {
        Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountingId);

        Task<PaymentHistoryDocument> GetByIdAsync(Guid accountingId, Guid operationId);

        Task RewriteAllAsync(Guid accountingId, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords);

        Task InsertOneAsync(Guid accountingId, PaymentOperationHistoryRecord payload);

        Task RemoveAsync(Guid accountingId);
    }
}
