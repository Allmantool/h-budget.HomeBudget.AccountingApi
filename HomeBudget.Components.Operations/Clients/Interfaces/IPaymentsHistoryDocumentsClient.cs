using System;
using System.Collections.Generic;
using System.Threading;
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

        Task<PaymentHistoryQueryResult> QueryAsync(
            Guid accountId,
            PaymentHistoryQuery query,
            CancellationToken cancellationToken);

        Task<PaymentHistoryDocument> GetByIdAsync(Guid accountId, Guid operationId);

        Task<ProjectionPublicationResult> PublishSnapshotAsync(
            PaymentHistoryProjectionSnapshot snapshot,
            Guid projectionRunId,
            CancellationToken cancellationToken);

        Task<AccountProjectionBalanceSnapshot> CreateAccountBalanceSnapshotAsync(
            Guid accountId,
            CancellationToken cancellationToken);

        Task<IReadOnlyCollection<PaymentHistoryProjectionScope>> DiscoverProjectionScopesAsync(
            CancellationToken cancellationToken);

        Task BeginProjectionRunAsync(ProjectionAuditRecord auditRecord);

        Task RecordProjectionPublicationAsync(
            Guid projectionRunId,
            string publicationState);

        Task CompleteProjectionRunAsync(
            Guid projectionRunId,
            string status,
            string error = null,
            string publicationState = null);

        Task InsertOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload);

        Task RemoveAsync(string financialPeriodIdentifier);

        Task<IEnumerable<PaymentHistoryDocument>> GetAllPeriodBalancesForAccountAsync(Guid accountId);

        Task ReplaceOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord document);

        Task BulkWriteAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> documents);
    }
}
