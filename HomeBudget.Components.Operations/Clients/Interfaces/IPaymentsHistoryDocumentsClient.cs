using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Clients.Interfaces
{
    public interface IPaymentsHistoryDocumentsClient : IDocumentClient
    {
        Task<PaymentHistoryDocument> GetLastForPeriodAsync(string financialPeriodIdentifier);

        Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountId, FinancialPeriod period = null);

        Task<PaymentHistoryDocument> GetByIdAsync(Guid accountId, Guid operationId);

        Task RewriteAllAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords);

        Task InsertOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload);

        Task RemoveAsync(string financialPeriodIdentifier);

        Task<IEnumerable<PaymentHistoryDocument>> GetAllPeriodBalancesForAccountAsync(Guid accountId);

        Task ReplaceOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord document);

        Task BulkWriteAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> documents);
    }
}
