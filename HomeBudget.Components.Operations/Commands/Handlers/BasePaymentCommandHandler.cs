using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Commands;
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
        IOutboxPaymentStatusService outboxPaymentStatusService)
    {
        protected Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
            where T : ICorrelatedCommand
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            paymentEvent.Metadata.Add(EventMetadataKeys.CorrelationId, request.CorrelationId);

            var payload = paymentEvent.Payload;
            var paymentAccountId = payload.PaymentAccountId;

            var createdAt = dateTimeProvider.GetNowUtc();

            var paymentMessageResult = PaymentEventToMessageConverter.Convert(paymentEvent, createdAt);

            var dbEntitity = new OutboxAccountPaymentsEntity
            {
                EventType = paymentEvent.EventType.ToString(),
                AggregateId = paymentAccountId.ToString(),
                PartitionKey = paymentEvent.Payload.GetPaymentAccountIdentifier(),
                Payload = JsonSerializer.Serialize(paymentEvent),
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };

            outboxPaymentStatusService.WriteRecord(dbEntitity);

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
