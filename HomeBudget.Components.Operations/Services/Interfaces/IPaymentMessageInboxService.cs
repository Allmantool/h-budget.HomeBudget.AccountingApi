using System;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Components.Operations.Services;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentMessageInboxService
    {
        Task<PaymentInboxStartResult> StartProcessingAsync(PaymentInboxMessageEntity message);

        Task MarkProcessedAsync(string messageId, DateTime processedUtc);

        Task<PaymentInboxFailureResult> MarkFailedAsync(
            string messageId,
            string lastError,
            int maxRetryAttempts,
            DateTime updatedUtc);

        Task MarkPoisonAsync(string messageId, string lastError, DateTime updatedUtc);

        Task RequestReplayAsync(string messageId, DateTime updatedUtc);
    }
}
