using System;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Handlers;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal class OutboxPaymentStatusService(
        ILogger<OutboxPaymentStatusService> logger,
        IDateTimeProvider dateTimeProvider,
        IExectutionStrategyHandler<IBaseWriteRepository> cdcWriteHandler)
        : IOutboxPaymentStatusService
    {
        public void WriteRecord(OutboxAccountPaymentsEntity record)
        {
            cdcWriteHandler.ExecuteFireAndForget(async cdcWriter =>
            {
                const string sql = @"
                    INSERT INTO dbo.OutboxAccountPayments
                    (
                        EventType,
                        AggregateId,
                        PartitionKey,
                        CorrelationId,
                        MessageId,
                        CausationId,
                        TraceParent,
                        TraceState,
                        Payload,
                        CreatedAt,
                        Status,
                        RetryCount
                    )
                    VALUES
                    (
                        @EventType,
                        @AggregateId,
                        @PartitionKey,
                        @CorrelationId,
                        @MessageId,
                        @CausationId,
                        @TraceParent,
                        @TraceState,
                        @Payload,
                        @CreatedAt,
                        @Status,
                        @RetryCount
                    );";

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
            });
        }

        public void SetStatus(string partitionKey, OutboxStatus status)
        {
            cdcWriteHandler.ExecuteFireAndForget(async cdcWriter =>
            {
                const string updateSql = @"
                                UPDATE dbo.OutboxAccountPayments
                                   SET Status = @Status,
                                       UpdatedAt = @UpdatedAt,
                                       PublishedAt = CASE
                                           WHEN @Status = 1 AND PublishedAt IS NULL THEN @UpdatedAt
                                           ELSE PublishedAt
                                       END,
                                       ProcessedAt = CASE
                                           WHEN @Status = 2 THEN @UpdatedAt
                                           ELSE ProcessedAt
                                       END
                                 WHERE PartitionKey = @PartitionKey;
                            ";
                var updatedAt = dateTimeProvider.GetNowUtc();

                try
                {
                    await cdcWriter.ExecuteAsync(updateSql, new OutboxStatusUpdateEntity
                    {
                        Status = status.Key,
                        UpdatedAt = updatedAt,
                        PartitionKey = partitionKey
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
            });
        }
    }
}
