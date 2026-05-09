using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IOutboxPaymentStatusService
    {
        Task WriteRecordAsync(OutboxAccountPaymentsEntity record);

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
    }
}
