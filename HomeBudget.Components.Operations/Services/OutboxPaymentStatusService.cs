using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal class OutboxPaymentStatusService(
        ILogger<OutboxPaymentStatusService> logger,
        IDateTimeProvider dateTimeProvider,
        IBaseWriteRepository cdcWriter,
        IBaseReadRepository cdcReader,
        IActiveLedgerGenerationResolver activeGenerationResolver = null)
        : IOutboxPaymentStatusService
    {
        private const int LastErrorMaxLength = 500;

        public async Task WriteRecordAsync(OutboxAccountPaymentsEntity record)
        {
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [HomeBudget.DataProtection].dbo.ActiveLedgerGenerations WITH (HOLDLOCK)
                    WHERE EnvironmentId = @EnvironmentId
                      AND LedgerId = @LedgerId
                      AND GenerationId = @LedgerGenerationId
                      AND GenerationToken = @GenerationTokenValue)
                BEGIN
                    THROW 51001, 'The ledger generation changed before durable command acceptance.', 1;
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.OutboxAccountPayments WITH (UPDLOCK, HOLDLOCK)
                    WHERE LedgerId = @LedgerId
                      AND LedgerGenerationId = @LedgerGenerationId
                      AND MessageId = @MessageId
                )
                BEGIN
                    INSERT INTO dbo.OutboxAccountPayments
                    (
                        LedgerId,
                        LedgerGenerationId,
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
                        @LedgerId,
                        @LedgerGenerationId,
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
                END

                COMMIT TRANSACTION;";

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
            catch (SqlException ex) when (ex.Number == 51001)
            {
                logger.CdcWriteFailed(
                    nameof(OutboxAccountPaymentsEntity),
                    ex.Message,
                    ex);
                throw new LedgerGenerationChangedException(ex);
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

        public async Task WriteTransferRecordsAsync(
            OutboxAccountPaymentsEntity sender,
            OutboxAccountPaymentsEntity recipient)
        {
            if (!string.Equals(sender.EnvironmentId, recipient.EnvironmentId, StringComparison.Ordinal) ||
                !string.Equals(sender.LedgerId, recipient.LedgerId, StringComparison.Ordinal) ||
                !string.Equals(sender.LedgerGenerationId, recipient.LedgerGenerationId, StringComparison.Ordinal) ||
                !string.Equals(sender.GenerationTokenValue, recipient.GenerationTokenValue, StringComparison.Ordinal))
            {
                throw new ArgumentException("Transfer outbox rows must use one ledger generation scope.");
            }

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [HomeBudget.DataProtection].dbo.ActiveLedgerGenerations WITH (HOLDLOCK)
                    WHERE EnvironmentId = @EnvironmentId
                      AND LedgerId = @LedgerId
                      AND GenerationId = @LedgerGenerationId
                      AND GenerationToken = @GenerationTokenValue)
                BEGIN
                    THROW 51001, 'The ledger generation changed before durable transfer acceptance.', 1;
                END

                INSERT INTO dbo.OutboxAccountPayments
                (
                    LedgerId, LedgerGenerationId, EventType, AggregateId, OperationId, PartitionKey,
                    CorrelationId, MessageId, IdempotencyKeyHash, RequestFingerprint, CommandType,
                    CausationId, TraceParent, TraceState, Payload, CreatedAt, UpdatedAt,
                    CreatedUtc, UpdatedUtc, Status, RetryCount
                )
                VALUES
                (
                    @LedgerId, @LedgerGenerationId, @SenderEventType, @SenderAggregateId, @SenderOperationId,
                    @SenderPartitionKey, @SenderCorrelationId, @SenderMessageId, @SenderIdempotencyKeyHash,
                    @SenderRequestFingerprint, @SenderCommandType, @SenderCausationId, @SenderTraceParent,
                    @SenderTraceState, @SenderPayload, @SenderCreatedAt, @SenderUpdatedAt,
                    @SenderCreatedUtc, @SenderUpdatedUtc, @SenderStatus, @SenderRetryCount
                ),
                (
                    @LedgerId, @LedgerGenerationId, @RecipientEventType, @RecipientAggregateId,
                    @RecipientOperationId, @RecipientPartitionKey, @RecipientCorrelationId,
                    @RecipientMessageId, @RecipientIdempotencyKeyHash, @RecipientRequestFingerprint,
                    @RecipientCommandType, @RecipientCausationId, @RecipientTraceParent,
                    @RecipientTraceState, @RecipientPayload, @RecipientCreatedAt, @RecipientUpdatedAt,
                    @RecipientCreatedUtc, @RecipientUpdatedUtc, @RecipientStatus, @RecipientRetryCount
                );

                COMMIT TRANSACTION;
                SELECT 2;";

            try
            {
                await cdcReader.SingleAsync<int>(sql, new
                {
                    sender.EnvironmentId,
                    sender.LedgerId,
                    sender.LedgerGenerationId,
                    sender.GenerationTokenValue,
                    SenderEventType = sender.EventType,
                    SenderAggregateId = sender.AggregateId,
                    SenderOperationId = sender.OperationId,
                    SenderPartitionKey = sender.PartitionKey,
                    SenderCorrelationId = sender.CorrelationId,
                    SenderMessageId = sender.MessageId,
                    SenderIdempotencyKeyHash = sender.IdempotencyKeyHash,
                    SenderRequestFingerprint = sender.RequestFingerprint,
                    SenderCommandType = sender.CommandType,
                    SenderCausationId = sender.CausationId,
                    SenderTraceParent = sender.TraceParent,
                    SenderTraceState = sender.TraceState,
                    SenderPayload = sender.Payload,
                    SenderCreatedAt = sender.CreatedAt,
                    SenderUpdatedAt = sender.UpdatedAt,
                    SenderCreatedUtc = sender.CreatedUtc,
                    SenderUpdatedUtc = sender.UpdatedUtc,
                    SenderStatus = sender.Status,
                    SenderRetryCount = sender.RetryCount,
                    RecipientEventType = recipient.EventType,
                    RecipientAggregateId = recipient.AggregateId,
                    RecipientOperationId = recipient.OperationId,
                    RecipientPartitionKey = recipient.PartitionKey,
                    RecipientCorrelationId = recipient.CorrelationId,
                    RecipientMessageId = recipient.MessageId,
                    RecipientIdempotencyKeyHash = recipient.IdempotencyKeyHash,
                    RecipientRequestFingerprint = recipient.RequestFingerprint,
                    RecipientCommandType = recipient.CommandType,
                    RecipientCausationId = recipient.CausationId,
                    RecipientTraceParent = recipient.TraceParent,
                    RecipientTraceState = recipient.TraceState,
                    RecipientPayload = recipient.Payload,
                    RecipientCreatedAt = recipient.CreatedAt,
                    RecipientUpdatedAt = recipient.UpdatedAt,
                    RecipientCreatedUtc = recipient.CreatedUtc,
                    RecipientUpdatedUtc = recipient.UpdatedUtc,
                    RecipientStatus = recipient.Status,
                    RecipientRetryCount = recipient.RetryCount
                });
            }
            catch (SqlException ex) when (ex.Number == 51001)
            {
                throw new LedgerGenerationChangedException(ex);
            }
        }

        public async Task<PaymentCommandRegistration> WriteIdempotentRecordAsync(OutboxAccountPaymentsEntity record)
        {
            const string sql = @"
                SET XACT_ABORT ON;

                BEGIN TRANSACTION;

                DECLARE @WasAlreadyAccepted bit = 0;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [HomeBudget.DataProtection].dbo.ActiveLedgerGenerations WITH (HOLDLOCK)
                    WHERE EnvironmentId = @EnvironmentId
                      AND LedgerId = @LedgerId
                      AND GenerationId = @LedgerGenerationId
                      AND GenerationToken = @GenerationTokenValue)
                BEGIN
                    THROW 51001, 'The ledger generation changed before durable command acceptance.', 1;
                END

                IF EXISTS (
                    SELECT 1
                    FROM dbo.OutboxAccountPayments WITH (UPDLOCK, HOLDLOCK)
                    WHERE AggregateId = @AggregateId
                      AND LedgerId = @LedgerId
                      AND LedgerGenerationId = @LedgerGenerationId
                      AND IdempotencyKeyHash = @IdempotencyKeyHash
                )
                BEGIN
                    SET @WasAlreadyAccepted = 1;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.OutboxAccountPayments
                    (
                        LedgerId, LedgerGenerationId, EventType, AggregateId, OperationId, PartitionKey, CorrelationId,
                        MessageId, IdempotencyKeyHash, RequestFingerprint, CommandType,
                        CausationId, TraceParent, TraceState, Payload, CreatedAt, UpdatedAt,
                        CreatedUtc, UpdatedUtc, Status, RetryCount
                    )
                    VALUES
                    (
                        @LedgerId, @LedgerGenerationId, @EventType, @AggregateId, @OperationId, @PartitionKey, @CorrelationId,
                        @MessageId, @IdempotencyKeyHash, @RequestFingerprint, @CommandType,
                        @CausationId, @TraceParent, @TraceState, @Payload, @CreatedAt, @UpdatedAt,
                        @CreatedUtc, @UpdatedUtc, @Status, @RetryCount
                    );
                END

                COMMIT TRANSACTION;

                SELECT
                    MessageId AS CommandId,
                    CAST(OperationId AS uniqueidentifier) AS OperationId,
                    @WasAlreadyAccepted AS WasAlreadyAccepted,
                    RequestFingerprint
                FROM dbo.OutboxAccountPayments
                WHERE AggregateId = @AggregateId
                  AND LedgerId = @LedgerId
                  AND LedgerGenerationId = @LedgerGenerationId
                  AND IdempotencyKeyHash = @IdempotencyKeyHash;";

            try
            {
                return await cdcReader.SingleAsync<PaymentCommandRegistration>(sql, record);
            }
            catch (SqlException ex) when (ex.Number == 51001)
            {
                throw new LedgerGenerationChangedException(ex);
            }
        }

        public async Task<PaymentCommandRecord> GetCommandAsync(Guid paymentAccountId, string commandId)
        {
            var generation = await GetActiveGenerationAsync();
            return await GetCommandAsync(paymentAccountId, commandId, generation);
        }

        public async Task<PaymentCommandRecord> GetCommandAsync(
            Guid paymentAccountId,
            string commandId,
            LedgerGenerationScope generation)
        {
            const string sql = @"
                SELECT TOP (1)
                    LedgerId, LedgerGenerationId,
                    MessageId AS CommandId,
                    CAST(AggregateId AS uniqueidentifier) AS PaymentAccountId,
                    CAST(OperationId AS uniqueidentifier) AS PaymentOperationId,
                    CommandType,
                    RequestFingerprint,
                    Status,
                    CreatedUtc AS AcceptedUtc,
                    PublishedUtc,
                    PersistedUtc,
                    ProjectedUtc
                FROM dbo.OutboxAccountPayments
                WHERE AggregateId = @PaymentAccountId
                  AND LedgerId = @LedgerId
                  AND LedgerGenerationId = @LedgerGenerationId
                  AND MessageId = @CommandId;";

            return await GetCommandAsync(
                sql,
                new
                {
                    PaymentAccountId = paymentAccountId.ToString(),
                    CommandId = commandId,
                    generation.LedgerId,
                    generation.LedgerGenerationId
                });
        }

        public async Task<PaymentCommandRecord> GetCommandByIdempotencyKeyAsync(Guid paymentAccountId, string idempotencyKeyHash)
        {
            var generation = await GetActiveGenerationAsync();
            return await GetCommandByIdempotencyKeyAsync(paymentAccountId, idempotencyKeyHash, generation);
        }

        public async Task<PaymentCommandRecord> GetCommandByIdempotencyKeyAsync(
            Guid paymentAccountId,
            string idempotencyKeyHash,
            LedgerGenerationScope generation)
        {
            const string sql = @"
                SELECT TOP (1)
                    LedgerId, LedgerGenerationId,
                    MessageId AS CommandId,
                    CAST(AggregateId AS uniqueidentifier) AS PaymentAccountId,
                    CAST(OperationId AS uniqueidentifier) AS PaymentOperationId,
                    CommandType,
                    Status,
                    CreatedUtc AS AcceptedUtc,
                    PublishedUtc,
                    PersistedUtc,
                    ProjectedUtc,
                    RequestFingerprint
                FROM dbo.OutboxAccountPayments
                WHERE AggregateId = @PaymentAccountId
                  AND LedgerId = @LedgerId
                  AND LedgerGenerationId = @LedgerGenerationId
                  AND IdempotencyKeyHash = @IdempotencyKeyHash;";

            return await GetCommandAsync(
                sql,
                new
                {
                    PaymentAccountId = paymentAccountId.ToString(),
                    IdempotencyKeyHash = idempotencyKeyHash,
                    generation.LedgerId,
                    generation.LedgerGenerationId
                });
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
                      AND LedgerId = @LedgerId
                      AND LedgerGenerationId = @LedgerGenerationId
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

            var generation = await GetActiveGenerationAsync();
            var parameters = new OutboxLockParameters
            {
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
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
                   SET Status = CASE
                            WHEN Status IN (@PersistedStatus, @ProjectedStatus, @DeadLetterStatus) THEN Status
                            ELSE @PublishedStatus
                        END,
                       UpdatedAt = @PublishedUtc,
                       UpdatedUtc = @PublishedUtc,
                       PublishedAt = COALESCE(PublishedAt, @PublishedUtc),
                       PublishedUtc = COALESCE(PublishedUtc, @PublishedUtc),
                       LastError = NULL,
                       LockedBy = NULL,
                       LockedUntilUtc = NULL
                 WHERE LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId
                   AND MessageId = @MessageId
                   AND LockedBy = @LockedBy;";

            var generation = await GetActiveGenerationAsync();
            await cdcWriter.ExecuteAsync(sql, new OutboxPublishUpdateEntity
            {
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                MessageId = messageId,
                LockedBy = lockedBy,
                PublishedStatus = OutboxStatus.Published.Key,
                PersistedStatus = OutboxStatus.Persisted.Key,
                ProjectedStatus = OutboxStatus.Projected.Key,
                DeadLetterStatus = OutboxStatus.DeadLettered.Key,
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
                 WHERE LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId
                   AND MessageId = @MessageId
                   AND LockedBy = @LockedBy
                   AND Status < @PersistedStatus;";

            var generation = await GetActiveGenerationAsync();
            await cdcWriter.ExecuteAsync(sql, new OutboxFailureUpdateEntity
            {
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                MessageId = messageId,
                LockedBy = lockedBy,
                LastError = Truncate(lastError, LastErrorMaxLength),
                MaxRetryAttempts = maxRetryAttempts,
                FailedStatus = OutboxStatus.Failed.Key,
                DeadLetterStatus = OutboxStatus.DeadLettered.Key,
                PersistedStatus = OutboxStatus.Persisted.Key,
                UpdatedUtc = updatedUtc
            });

            TelemetryMetrics.OutboxStatusTransitions.Add(1, [new("status", "FailedOrDeadLettered")]);
        }

        public async Task SetStatusAsync(string messageId, OutboxStatus status)
        {
            const string updateSql = @"
                UPDATE dbo.OutboxAccountPayments
                   SET Status = CASE
                            WHEN Status = @DeadLetterStatus THEN Status
                            WHEN Status IN (@PersistedStatus, @ProjectedStatus) THEN Status
                            WHEN @Status = @DeadLetterStatus THEN @DeadLetterStatus
                            WHEN @Status > Status THEN @Status
                            ELSE Status
                        END,
                       UpdatedAt = @UpdatedAt,
                       UpdatedUtc = @UpdatedAt,
                       PublishedAt = CASE
                            WHEN @Status = 1 AND Status = 0 AND PublishedAt IS NULL THEN @UpdatedAt
                           ELSE PublishedAt
                       END,
                       PublishedUtc = CASE
                            WHEN @Status = 1 AND Status = 0 AND PublishedUtc IS NULL THEN @UpdatedAt
                           ELSE PublishedUtc
                       END
                 WHERE LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId
                   AND MessageId = @MessageId;";
            var updatedAt = dateTimeProvider.GetNowUtc();
            var generation = await GetActiveGenerationAsync();

            try
            {
                await cdcWriter.ExecuteAsync(updateSql, new OutboxStatusUpdateEntity
                {
                    LedgerId = generation.LedgerId,
                    LedgerGenerationId = generation.LedgerGenerationId,
                    Status = status.Key,
                    PersistedStatus = OutboxStatus.Persisted.Key,
                    ProjectedStatus = OutboxStatus.Projected.Key,
                    DeadLetterStatus = OutboxStatus.DeadLettered.Key,
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

        public Task MarkPersistedAsync(string messageId, DateTime persistedUtc)
        {
            return SetLifecycleStatusAsync(messageId, OutboxStatus.Persisted, persistedUtc, "PersistedUtc");
        }

        public Task MarkProjectedAsync(string messageId, DateTime projectedUtc)
        {
            return SetLifecycleStatusAsync(messageId, OutboxStatus.Projected, projectedUtc, "ProjectedUtc");
        }

        public async Task MarkDeadLetteredAsync(string messageId, string lastError, DateTime updatedUtc)
        {
            const string sql = @"
                UPDATE dbo.OutboxAccountPayments
                   SET Status = @Status,
                       LastError = @LastError,
                       UpdatedAt = @UpdatedUtc,
                       UpdatedUtc = @UpdatedUtc
                 WHERE LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId
                   AND MessageId = @MessageId
                   AND Status < @PersistedStatus;";

            var generation = await GetActiveGenerationAsync();
            await cdcWriter.ExecuteAsync(sql, new OutboxCommandLifecycleUpdateEntity
            {
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                MessageId = messageId,
                Status = OutboxStatus.DeadLettered.Key,
                PersistedStatus = OutboxStatus.Persisted.Key,
                LastError = Truncate(lastError, LastErrorMaxLength),
                UpdatedUtc = updatedUtc
            });
        }

        private async Task<PaymentCommandRecord> GetCommandAsync(string sql, object parameters)
        {
            var result = await cdcReader.GetAsync<PaymentCommandRecord>(sql, parameters);
            var row = result.FirstOrDefault();
            if (row is null)
            {
                return null;
            }

            return row with { Status = ToCommandStatus((byte)row.Status) };
        }

        private async Task SetLifecycleStatusAsync(
            string messageId,
            OutboxStatus status,
            DateTime updatedUtc,
            string completedAtColumn)
        {
            var sql = $@"
                UPDATE dbo.OutboxAccountPayments
                   SET Status = CASE
                            WHEN Status = @DeadLetterStatus OR Status > @Status THEN Status
                            ELSE @Status
                        END,
                        {completedAtColumn} = CASE
                            WHEN Status = @DeadLetterStatus OR Status > @Status THEN {completedAtColumn}
                            ELSE COALESCE({completedAtColumn}, @UpdatedUtc)
                        END,
                       UpdatedAt = @UpdatedUtc,
                       UpdatedUtc = @UpdatedUtc
                 WHERE LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId
                   AND MessageId = @MessageId
                   AND Status <> @DeadLetterStatus;";

            var generation = await GetActiveGenerationAsync();
            await cdcWriter.ExecuteAsync(sql, new OutboxCommandLifecycleUpdateEntity
            {
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                MessageId = messageId,
                Status = status.Key,
                DeadLetterStatus = OutboxStatus.DeadLettered.Key,
                UpdatedUtc = updatedUtc
            });
        }

        private static PaymentCommandStatus ToCommandStatus(byte status)
        {
            return status switch
            {
                1 => PaymentCommandStatus.Published,
                5 => PaymentCommandStatus.Failed,
                6 => PaymentCommandStatus.Persisted,
                7 => PaymentCommandStatus.Projected,
                _ => PaymentCommandStatus.Accepted
            };
        }

        private Task<LedgerGenerationScope> GetActiveGenerationAsync()
        {
            return activeGenerationResolver?.GetActiveAsync()
                ?? Task.FromResult(LedgerGenerationScope.Legacy);
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
