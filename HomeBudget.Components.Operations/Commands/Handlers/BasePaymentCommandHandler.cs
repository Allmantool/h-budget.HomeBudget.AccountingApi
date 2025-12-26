using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Handlers;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal abstract class BasePaymentCommandHandler(
        ILogger logger,
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IExectutionStrategyHandler<IKafkaProducer<string, string>> kafkaHandler,
        IExectutionStrategyHandler<IBaseWriteRepository> cdcHandler)
    {
        protected Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            var payload = paymentEvent.Payload;
            var paymentAccountId = payload.PaymentAccountId;

            var createdAt = dateTimeProvider.GetNowUtc();

            var paymentMessageResult = PaymentEventToMessageConverter.Convert(paymentEvent, createdAt);

            cdcHandler.ExecuteFireAndForget(async cdcWriter =>
            {
                var dbEntitity = new OutboxAccountPaymentsEntity
                {
                    EventType = paymentEvent.EventType.ToString(),
                    AggregateId = paymentAccountId.ToString(),
                    PartitionKey = paymentEvent.Payload.GetPaymentAccountIdentifier(),
                    Payload = JsonSerializer.Serialize(paymentEvent),
                    CreatedAt = createdAt,
                };

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
                    await cdcWriter.ExecuteAsync(sql, dbEntitity);
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

            kafkaHandler.ExecuteAndWaitAsync(async producer =>
            {
                var message = paymentMessageResult.Payload;

                try
                {
                    var deliveryResult = await producer.ProduceAsync(BaseTopics.AccountingPayments, message, cancellationToken);
                }
                catch (Exception ex)
                {
                    var reason = ex.InnerException?.Message ?? ex.Message;

                    logger.ProduceFailed(
                        BaseTopics.AccountingPayments,
                        message.Key,
                        reason,
                        ex);
                }
            });

            return Task.FromResult(Result<Guid>.Succeeded(paymentEvent.Payload.Key));
        }
    }
}
