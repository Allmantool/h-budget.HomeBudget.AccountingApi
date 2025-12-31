using System;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Handlers;

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
                        @Payload,
                        @CreatedAt,
                        @Status,
                        @RetryCount
                    );";

                try
                {
                    await cdcWriter.ExecuteAsync(sql, record);
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
                var updateSql = $@"
                                UPDATE dbo.OutboxAccountPayments
                                   SET Status = {status.Key},
                                       UpdatedAt = '{dateTimeProvider.GetNowUtc()}'
                                 WHERE PartitionKey = '{partitionKey}';
                            ";

                try
                {
                    await cdcWriter.ExecuteAsync(updateSql);
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
