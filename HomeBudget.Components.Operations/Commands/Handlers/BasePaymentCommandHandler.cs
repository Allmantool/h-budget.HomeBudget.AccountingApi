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
            if (paymentEvent.EnvelopId == Guid.Empty)
            {
                paymentEvent.EnvelopId = Guid.NewGuid();
            }

            var payload = paymentEvent.Payload;
            var paymentAccountId = payload.PaymentAccountId;
            var createdAt = dateTimeProvider.GetNowUtc();
            var messageId = paymentEvent.EnvelopId.ToString();
            var causationId = activity?.SpanId.ToString();
            var propagationCarrier = TraceContextPropagation.Capture(activity);

            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = request.CorrelationId;
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = messageId;
            paymentEvent.Metadata[EventMetadataKeys.CausationId] = causationId ?? string.Empty;

            if (activity != null)
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceId] = activity.TraceId.ToString();
                if (propagationCarrier.TryGetValue(TraceContextPropagation.TraceParent, out var traceParent))
                {
                    paymentEvent.Metadata[EventMetadataKeys.TraceParent] = traceParent;
                }

                if (propagationCarrier.TryGetValue(TraceContextPropagation.TraceState, out var traceState))
                {
                    paymentEvent.Metadata[EventMetadataKeys.TraceState] = traceState;
                }

                if (propagationCarrier.TryGetValue(TraceContextPropagation.Baggage, out var baggage))
                {
                    paymentEvent.Metadata[EventMetadataKeys.Baggage] = baggage;
                }

                activity.SetCorrelationId(request.CorrelationId);
                activity.SetAccount(paymentAccountId);
                activity.SetPayment(paymentEvent.Payload.Key);
                activity.SetTag(ActivityTags.MessagingSystem, "kafka");
                activity.SetTag(ActivityTags.KafkaTopic, BaseTopics.AccountingPayments);
                activity.SetTag(ActivityTags.MessagingOperation, "publish");
                activity.SetTag("messaging.message_id", messageId);
                activity.SetTag("messaging.conversation_id", causationId);
                activity.SetTag("outbox.partition_key", paymentEvent.Payload.GetPaymentAccountIdentifier());
            }

            var paymentMessageResult = PaymentEventToMessageConverter.Convert(paymentEvent, createdAt);

            var dbEntity = new OutboxAccountPaymentsEntity
            {
                EventType = paymentEvent.EventType.ToString(),
                AggregateId = paymentAccountId.ToString(),
                PartitionKey = paymentEvent.Payload.GetPaymentAccountIdentifier(),
                CorrelationId = request.CorrelationId,
                MessageId = messageId,
                CausationId = causationId,
                TraceParent = paymentEvent.Metadata.Get(EventMetadataKeys.TraceParent),
                TraceState = paymentEvent.Metadata.Get(EventMetadataKeys.TraceState),
                Payload = JsonSerializer.Serialize(paymentEvent),
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };

            using (var outboxActivity = ActivityPropagation.StartActivity("outbox.write", ActivityKind.Internal))
            {
                var outboxStopwatch = Stopwatch.StartNew();
                if (outboxActivity != null)
                {
                    outboxActivity.SetCorrelationId(request.CorrelationId);
                    outboxActivity.SetAccount(paymentAccountId);
                    outboxActivity.SetPayment(paymentEvent.Payload.Key);
                    outboxActivity.SetTag("db.system", "outbox");
                    outboxActivity.SetTag("outbox.partition_key", paymentEvent.Payload.GetPaymentAccountIdentifier());
                    outboxActivity.SetTag("messaging.message_id", messageId);
                }

                outboxPaymentStatusService.WriteRecord(dbEntity);
                outboxStopwatch.Stop();
                TelemetryMetrics.OutboxWriteDurationMs.Record(
                    outboxStopwatch.Elapsed.TotalMilliseconds,
                    [new("event_type", paymentEvent.EventType.ToString())]);
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
