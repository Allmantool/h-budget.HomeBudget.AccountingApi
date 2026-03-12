using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Extensions;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Handlers;
using HomeBudget.Core.Models;
using HomeBudget.Core.Observability;

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
            using var activity = Telemetry.ActivitySource.StartActivity(ActivityNames.Kafka.Produce, ActivityKind.Producer);

            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);
            var payload = paymentEvent.Payload;
            var paymentAccountId = payload.PaymentAccountId;
            var createdAt = dateTimeProvider.GetNowUtc();

            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = request.CorrelationId;

            if (activity != null)
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceId] = activity.TraceId.ToString();
                paymentEvent.Metadata[EventMetadataKeys.TraceParent] = activity.Id;
                activity.SetCorrelationId(request.CorrelationId);
                activity.SetAccount(paymentAccountId);
                activity.SetPayment(paymentEvent.Payload.Key);
                activity.SetTag("messaging.system", "kafka");
                activity.SetTag("messaging.destination", BaseTopics.AccountingPayments);
                activity.SetTag("outbox.partition_key", paymentEvent.Payload.GetPaymentAccountIdentifier());
            }

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

            using (var outboxActivity = ActivityPropagation.StartActivity("outbox.write", ActivityKind.Internal))
            {
                if (outboxActivity != null)
                {
                    outboxActivity.SetCorrelationId(request.CorrelationId);
                    outboxActivity.SetAccount(paymentAccountId);
                    outboxActivity.SetPayment(paymentEvent.Payload.Key);
                    outboxActivity.SetTag("db.system", "outbox");
                    outboxActivity.SetTag("outbox.partition_key", paymentEvent.Payload.GetPaymentAccountIdentifier());
                }

                outboxPaymentStatusService.WriteRecord(dbEntity);
                outboxActivity?.SetStatus(ActivityStatusCode.Ok);
                outboxActivity?.AddEvent(new("outbox.persisted"));
            }

            await kafkaHandler.ExecuteAndWaitAsync(async producer =>
            {
                var message = paymentMessageResult.Payload;

                try
                {
                    await producer.ProduceAsync(
                        BaseTopics.AccountingPayments,
                        message,
                        cancellationToken);

                    activity?.AddEvent(ActivityEvents.KafkaPublished);

                    logger.ProduceMessageSuccessfully(BaseTopics.AccountingPayments, message.Key);
                }
                catch (Exception ex)
                {
                    activity?.RecordException(ex);

                    var reason = ex.InnerException?.Message ?? ex.Message;
                    logger.ProduceFailed(BaseTopics.AccountingPayments, message.Key, reason, ex);
                }
            });

            activity?.SetStatus(ActivityStatusCode.Ok);

            return Result<Guid>.Succeeded(paymentEvent.Payload.Key);
        }
    }
}
