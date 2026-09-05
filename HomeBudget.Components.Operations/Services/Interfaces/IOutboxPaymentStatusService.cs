using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IOutboxPaymentStatusService
    {
        Task WriteRecordAsync(OutboxAccountPaymentsEntity record);

        Task<PaymentCommandRegistration> WriteIdempotentRecordAsync(OutboxAccountPaymentsEntity record);

        Task<PaymentCommandRecord> GetCommandAsync(Guid paymentAccountId, string commandId);

        Task<PaymentCommandRecord> GetCommandByIdempotencyKeyAsync(Guid paymentAccountId, string idempotencyKeyHash);

        Task<IReadOnlyCollection<OutboxAccountPaymentsEntity>> LockRetryableRowsAsync(
            string lockedBy,
            DateTime nowUtc,
            DateTime lockedUntilUtc,
            int batchSize,
            int maxRetryAttempts);

        Task MarkPublishedAsync(
            string messageId,
            string lockedBy,
            DateTime publishedUtc);

        Task MarkFailedAsync(
            string messageId,
            string lockedBy,
            string lastError,
            int maxRetryAttempts,
            DateTime updatedUtc);

        Task SetStatusAsync(string messageId, OutboxStatus status);

        Task MarkPersistedAsync(string messageId, DateTime persistedUtc);

        Task MarkProjectedAsync(string messageId, DateTime projectedUtc);

        Task MarkDeadLetteredAsync(string messageId, string lastError, DateTime updatedUtc);
    }
}
