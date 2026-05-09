using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Options;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Constants;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOutboxPublisher(
        ILogger<PaymentOutboxPublisher> logger,
        IOutboxPaymentStatusService outboxPaymentStatusService,
        IKafkaProducer<string, string> kafkaProducer,
        IDateTimeProvider dateTimeProvider,
        IOptions<PaymentOutboxPublisherOptions> options)
    {
        private readonly string _lockedBy = $"{Environment.MachineName}-{Guid.NewGuid():N}";

        public async Task<int> PublishRetryableRowsAsync(CancellationToken cancellationToken)
        {
            var publisherOptions = options.Value;
            var nowUtc = dateTimeProvider.GetNowUtc();
            var lockedRows = await outboxPaymentStatusService.LockRetryableRowsAsync(
                _lockedBy,
                nowUtc,
                nowUtc.AddSeconds(publisherOptions.LockTimeoutSeconds),
                publisherOptions.BatchSize,
                publisherOptions.MaxRetryAttempts);

            var publishedCount = 0;
            foreach (var row in lockedRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await PublishRowAsync(row, publisherOptions.MaxRetryAttempts, cancellationToken))
                {
                    publishedCount++;
                }
            }

            return publishedCount;
        }

        private async Task<bool> PublishRowAsync(
            OutboxAccountPaymentsEntity row,
            int maxRetryAttempts,
            CancellationToken cancellationToken)
        {
            try
            {
                var paymentEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(row.Payload);
                var messageResult = PaymentEventToMessageConverter.Convert(paymentEvent, row.CreatedUtc);
                if (!messageResult.IsSucceeded)
                {
                    throw new InvalidOperationException(messageResult.StatusMessage);
                }

                await kafkaProducer.ProduceAsync(
                    BaseTopics.AccountingPayments,
                    messageResult.Payload,
                    cancellationToken);

                await outboxPaymentStatusService.MarkPublishedAsync(
                    row.MessageId,
                    _lockedBy,
                    dateTimeProvider.GetNowUtc());

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.InnerException?.Message ?? ex.Message;
                logger.LogError(
                    ex,
                    "Outbox payment publish failed. MessageId={MessageId}, Reason={Reason}",
                    row.MessageId,
                    reason);

                await outboxPaymentStatusService.MarkFailedAsync(
                    row.MessageId,
                    _lockedBy,
                    reason,
                    maxRetryAttempts,
                    dateTimeProvider.GetNowUtc());

                return false;
            }
        }
    }
}
