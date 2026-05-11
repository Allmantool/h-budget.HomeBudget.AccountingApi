using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal class OutboxPaymentStatusService(
        ILogger<OutboxPaymentStatusService> logger,
        IDateTimeProvider dateTimeProvider,
        IBaseWriteRepository cdcWriter,
        IBaseReadRepository cdcReader)
        : IOutboxPaymentStatusService
    {
        private const int LastErrorMaxLength = 500;

        public async Task WriteRecordAsync(OutboxAccountPaymentsEntity record)
        {
            const string sql = @"
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.OutboxAccountPayments WITH (UPDLOCK, HOLDLOCK)
                    WHERE MessageId = @MessageId
                )
                BEGIN
                    INSERT INTO dbo.OutboxAccountPayments
                    (
                        EventType,
                        AggregateId,
                        OperationId,
                        PartitionKey,
                        CorrelationId,
                        MessageId,
                        CausationId,
                        TraceParent,
                        TraceState,
                        Payload,
                        CreatedAt,
                        UpdatedAt,
                        CreatedUtc,
                        UpdatedUtc,
                        Status,
                        RetryCount
                    )
                    VALUES
                    (
                        @EventType,
                        @AggregateId,
                        @OperationId,
                        @PartitionKey,
                        @CorrelationId,
                        @MessageId,
                        @CausationId,
                        @TraceParent,
                        @TraceState,
                        @Payload,
                        @CreatedAt,
                        @UpdatedAt,
                        @CreatedUtc,
                        @UpdatedUtc,
                        @Status,
                        @RetryCount
                    );
                END";

            try
            {
                await cdcWriter.ExecuteAsync(sql, record);
                TelemetryMetrics.OutboxStatusTransitions.Add(
                    1,
                    [
                        new("status", OutboxStatus.Pending.Name),
                        new("event_type", record.EventType)
                    ]);
            }
            catch (Exception ex)
            {
                var reason = ex.InnerException?.Message ?? ex.Message;

                logger.CdcWriteFailed(
                    nameof(OutboxAccountPaymentsEntity),
                    reason,
                    ex);

                throw;
            }
        }

        public async Task<IReadOnlyCollection<OutboxAccountPaymentsEntity>> LockRetryableRowsAsync(
            string lockedBy,
            DateTime nowUtc,
            DateTime lockedUntilUtc,
            int batchSize,
            int maxRetryAttempts)
        {
            const string sql = @"
                ;WITH RetryableRows AS
                (
                    SELECT TOP (@BatchSize) *
                    FROM dbo.OutboxAccountPayments WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE Status IN (@PendingStatus, @FailedStatus)
                      AND RetryCount < @MaxRetryAttempts
                      AND (LockedUntilUtc IS NULL OR LockedUntilUtc < @NowUtc)
                    ORDER BY CreatedUtc, CreatedAt
                )
                UPDATE RetryableRows
                   SET LockedBy = @LockedBy,
                       LockedUntilUtc = @LockedUntilUtc,
                       UpdatedAt = @NowUtc,
                       UpdatedUtc = @NowUtc
                OUTPUT inserted.*;";

            var parameters = new OutboxLockParameters
            {
                PendingStatus = OutboxStatus.Pending.Key,
                FailedStatus = OutboxStatus.Failed.Key,
                MaxRetryAttempts = maxRetryAttempts,
                BatchSize = batchSize,
                LockedBy = lockedBy,
                NowUtc = nowUtc,
                LockedUntilUtc = lockedUntilUtc
            };

            return await cdcReader.GetAsync<OutboxAccountPaymentsEntity>(sql, parameters);
        }

        public async Task MarkPublishedAsync(
            string messageId,
            string lockedBy,
            DateTime publishedUtc)
        {
            const string sql = @"
                UPDATE dbo.OutboxAccountPayments
                   SET Status = @PublishedStatus,
                       UpdatedAt = @PublishedUtc,
                       UpdatedUtc = @PublishedUtc,
                       PublishedAt = COALESCE(PublishedAt, @PublishedUtc),
                       PublishedUtc = COALESCE(PublishedUtc, @PublishedUtc),
                       LastError = NULL,
                       LockedBy = NULL,
                       LockedUntilUtc = NULL
                 WHERE MessageId = @MessageId
                   AND LockedBy = @LockedBy;";

            await cdcWriter.ExecuteAsync(sql, new OutboxPublishUpdateEntity
            {
                MessageId = messageId,
                LockedBy = lockedBy,
                PublishedStatus = OutboxStatus.Published.Key,
                PublishedUtc = publishedUtc
            });

            TelemetryMetrics.OutboxStatusTransitions.Add(1, [new("status", OutboxStatus.Published.Name)]);
        }

        public async Task MarkFailedAsync(
            string messageId,
            string lockedBy,
            string lastError,
            int maxRetryAttempts,
            DateTime updatedUtc)
        {
            const string sql = @"
                UPDATE dbo.OutboxAccountPayments
                   SET RetryCount = RetryCount + 1,
                       Status = CASE
                           WHEN RetryCount + 1 >= @MaxRetryAttempts THEN @DeadLetterStatus
                           ELSE @FailedStatus
                       END,
                       LastError = @LastError,
                       UpdatedAt = @UpdatedUtc,
                       UpdatedUtc = @UpdatedUtc,
                       LockedBy = NULL,
                       LockedUntilUtc = NULL
                 WHERE MessageId = @MessageId
                   AND LockedBy = @LockedBy;";

            await cdcWriter.ExecuteAsync(sql, new OutboxFailureUpdateEntity
            {
                MessageId = messageId,
                LockedBy = lockedBy,
                LastError = Truncate(lastError, LastErrorMaxLength),
                MaxRetryAttempts = maxRetryAttempts,
                FailedStatus = OutboxStatus.Failed.Key,
                DeadLetterStatus = OutboxStatus.DeadLettered.Key,
                UpdatedUtc = updatedUtc
            });

            TelemetryMetrics.OutboxStatusTransitions.Add(1, [new("status", "FailedOrDeadLettered")]);
        }

        public async Task SetStatusAsync(string messageId, OutboxStatus status)
        {
            const string updateSql = @"
                UPDATE dbo.OutboxAccountPayments
                   SET Status = @Status,
                       UpdatedAt = @UpdatedAt,
                       UpdatedUtc = @UpdatedAt,
                       PublishedAt = CASE
                           WHEN @Status = 1 AND PublishedAt IS NULL THEN @UpdatedAt
                           ELSE PublishedAt
                       END,
                       PublishedUtc = CASE
                           WHEN @Status = 1 AND PublishedUtc IS NULL THEN @UpdatedAt
                           ELSE PublishedUtc
                       END
                 WHERE MessageId = @MessageId;";
            var updatedAt = dateTimeProvider.GetNowUtc();

            try
            {
                await cdcWriter.ExecuteAsync(updateSql, new OutboxStatusUpdateEntity
                {
                    Status = status.Key,
                    UpdatedAt = updatedAt,
                    MessageId = messageId
                });
                TelemetryMetrics.OutboxStatusTransitions.Add(1, [new("status", status.Name)]);
            }
            catch (Exception ex)
            {
                var reason = ex.InnerException?.Message ?? ex.Message;

                logger.CdcWriteFailed(
                    nameof(OutboxAccountPaymentsEntity),
                    reason,
                    ex);

                throw;
            }
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
