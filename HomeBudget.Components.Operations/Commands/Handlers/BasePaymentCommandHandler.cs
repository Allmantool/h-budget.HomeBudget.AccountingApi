using System;
using System.Diagnostics;
using System.Text;
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
        protected async Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
            where T : ICorrelatedCommand
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            var currentActivity = Activity.Current;

            var traceId = currentActivity?.TraceId.ToString();
            var traceParent = currentActivity?.Id;

            if (traceId != null)
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceId] = traceId;
            }

            if (traceParent != null)
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceParent] = traceParent;
            }

            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = request.CorrelationId;

            var payload = paymentEvent.Payload;
            var paymentAccountId = payload.PaymentAccountId;
            var createdAt = dateTimeProvider.GetNowUtc();

            var paymentMessageResult = PaymentEventToMessageConverter.Convert(paymentEvent, createdAt);

            var dbEntity = new OutboxAccountPaymentsEntity
            {
                EventType = paymentEvent.EventType.ToString(),
                AggregateId = paymentAccountId.ToString(),
                PartitionKey = paymentEvent.Payload.GetPaymentAccountIdentifier(),
                Payload = JsonSerializer.Serialize(paymentEvent),
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };

            outboxPaymentStatusService.WriteRecord(dbEntity);

            await kafkaHandler.ExecuteAndWaitAsync(async producer =>
            {
                var message = paymentMessageResult.Payload;

                if (traceParent != null)
                {
                    message.Headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
                }

                if (traceId != null)
                {
                    message.Headers.Add("traceId", Encoding.UTF8.GetBytes(traceId));
                }

                message.Headers.Add("correlationId", Encoding.UTF8.GetBytes(request.CorrelationId));

                try
                {
                    using var activity = Tracing.Source.StartActivity("kafka.produce", ActivityKind.Producer);

                    var deliveryResult = await producer.ProduceAsync(
                        BaseTopics.AccountingPayments,
                        message,
                        cancellationToken);

                    logger.LogInformation(
                        "Produced Kafka message to {Topic} (Key: {Key})",
                        BaseTopics.AccountingPayments,
                        message.Key);
                }
                catch (Exception ex)
                {
                    var reason = ex.InnerException?.Message ?? ex.Message;
                    logger.ProduceFailed(BaseTopics.AccountingPayments, message.Key, reason, ex);
                }
            });

            return Result<Guid>.Succeeded(paymentEvent.Payload.Key);
        }
    }
}