using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal sealed class PaymentMessageInboxService(
        ILogger<PaymentMessageInboxService> logger,
        IBaseReadRepository cdcReader,
        IBaseWriteRepository cdcWriter)
        : IPaymentMessageInboxService
    {
        private const int LastErrorMaxLength = 500;

        public async Task<PaymentInboxStartResult> StartProcessingAsync(PaymentInboxMessageEntity message)
        {
            const string sql = @"
                SET XACT_ABORT ON;

                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.PaymentInboxMessages WITH (UPDLOCK, HOLDLOCK)
                    WHERE MessageId = @MessageId
                )
                BEGIN
                    INSERT INTO dbo.PaymentInboxMessages
                    (
                        MessageId,
                        Topic,
                        Partition,
                        [Offset],
                        Status,
                        RetryCount,
                        LastError,
                        CreatedUtc,
                        UpdatedUtc,
                        ProcessedUtc,
                        RawMessage
                    )
                    VALUES
                    (
                        @MessageId,
                        @Topic,
                        @Partition,
                        @Offset,
                        @Status,
                        @RetryCount,
                        @LastError,
                        @CreatedUtc,
                        @UpdatedUtc,
                        @ProcessedUtc,
                        @RawMessage
                    );
                END
                ELSE IF EXISTS (
                    SELECT 1
                    FROM dbo.PaymentInboxMessages WITH (UPDLOCK, HOLDLOCK)
                    WHERE MessageId = @MessageId
                      AND Status NOT IN ('Processed', 'Poison')
                )
                BEGIN
                    UPDATE dbo.PaymentInboxMessages
                       SET Status = 'Processing',
                           UpdatedUtc = @UpdatedUtc,
                           RetryCount = CASE WHEN Status = 'ReplayRequested' THEN 0 ELSE RetryCount END,
                           LastError = CASE WHEN Status = 'ReplayRequested' THEN NULL ELSE LastError END,
                           ProcessedUtc = CASE WHEN Status = 'ReplayRequested' THEN NULL ELSE ProcessedUtc END
                     WHERE MessageId = @MessageId;
                END

                COMMIT TRANSACTION;

                SELECT MessageId,
                       Status,
                       RetryCount,
                       CAST(CASE WHEN Status IN ('Processed', 'Poison') THEN 0 ELSE 1 END AS bit) AS ShouldProcess
                  FROM dbo.PaymentInboxMessages
                 WHERE MessageId = @MessageId;";

            var result = await cdcReader.SingleAsync<PaymentInboxStartResult>(
                sql,
                message with
                {
                    Status = PaymentInboxStatus.Processing,
                    RetryCount = 0
                });

            if (!result.ShouldProcess)
            {
                logger.LogInformation(
                    "Payment inbox message skipped. MessageId={MessageId}, Status={Status}",
                    result.MessageId,
                    result.Status);
            }

            return result;
        }

        public async Task MarkProcessedAsync(string messageId, DateTime processedUtc)
        {
            const string sql = @"
                UPDATE dbo.PaymentInboxMessages
                   SET Status = @Status,
                       UpdatedUtc = @UpdatedUtc,
                       ProcessedUtc = COALESCE(ProcessedUtc, @UpdatedUtc),
                       LastError = NULL
                 WHERE MessageId = @MessageId;";

            await cdcWriter.ExecuteAsync(sql, new PaymentInboxMessageUpdateEntity
            {
                MessageId = messageId,
                Status = PaymentInboxStatus.Processed,
                UpdatedUtc = processedUtc
            });

            TelemetryMetrics.PaymentInboxStatusTransitions.Add(
                1,
                [new("status", PaymentInboxStatus.Processed)]);
        }

        public async Task<PaymentInboxFailureResult> MarkFailedAsync(
            string messageId,
            string lastError,
            int maxRetryAttempts,
            DateTime updatedUtc)
        {
            const string sql = @"
                UPDATE dbo.PaymentInboxMessages
                   SET RetryCount = RetryCount + 1,
                       Status = CASE
                           WHEN RetryCount + 1 >= @MaxRetryAttempts THEN 'Poison'
                           ELSE 'Failed'
                       END,
                       LastError = @LastError,
                       UpdatedUtc = @UpdatedUtc
                OUTPUT inserted.MessageId,
                       inserted.Status,
                       inserted.RetryCount
                 WHERE MessageId = @MessageId;";

            var results = await cdcReader.GetAsync<PaymentInboxFailureResult>(
                sql,
                new PaymentInboxMessageUpdateEntity
                {
                    MessageId = messageId,
                    LastError = Truncate(lastError, LastErrorMaxLength),
                    MaxRetryAttempts = maxRetryAttempts,
                    UpdatedUtc = updatedUtc
                });

            var result = results.Single();
            TelemetryMetrics.PaymentInboxStatusTransitions.Add(1, [new("status", result.Status)]);

            return result;
        }

        public async Task MarkPoisonAsync(string messageId, string lastError, DateTime updatedUtc)
        {
            const string sql = @"
                UPDATE dbo.PaymentInboxMessages
                   SET Status = @Status,
                       LastError = @LastError,
                       UpdatedUtc = @UpdatedUtc
                 WHERE MessageId = @MessageId;";

            await cdcWriter.ExecuteAsync(sql, new PaymentInboxMessageUpdateEntity
            {
                MessageId = messageId,
                Status = PaymentInboxStatus.Poison,
                LastError = Truncate(lastError, LastErrorMaxLength),
                UpdatedUtc = updatedUtc
            });

            TelemetryMetrics.PaymentInboxStatusTransitions.Add(1, [new("status", PaymentInboxStatus.Poison)]);
        }

        public async Task RequestReplayAsync(string messageId, DateTime updatedUtc)
        {
            const string sql = @"
                UPDATE dbo.PaymentInboxMessages
                   SET Status = @Status,
                       RetryCount = 0,
                       LastError = NULL,
                       ProcessedUtc = NULL,
                       UpdatedUtc = @UpdatedUtc
                 WHERE MessageId = @MessageId
                   AND Status = 'Poison';";

            await cdcWriter.ExecuteAsync(sql, new PaymentInboxMessageUpdateEntity
            {
                MessageId = messageId,
                Status = PaymentInboxStatus.ReplayRequested,
                UpdatedUtc = updatedUtc
            });

            TelemetryMetrics.PaymentInboxStatusTransitions.Add(1, [new("status", PaymentInboxStatus.ReplayRequested)]);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value[..maxLength];
        }
    }
}
