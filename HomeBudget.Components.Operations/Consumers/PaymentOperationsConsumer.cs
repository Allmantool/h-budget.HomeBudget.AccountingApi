using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Options;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Consumers
{
    internal class PaymentOperationsConsumer : BaseKafkaConsumer<string, string>
    {
        private static string paymentsConsumerGroup = "accounting.payments.group";

        private readonly ILogger<PaymentOperationsConsumer> _logger;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IEventStoreDbWriteClient<PaymentOperationEvent> _eventStoreDbWriteClient;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IPaymentMessageInboxService _messageInboxService;
        private readonly PaymentInboxOptions _paymentInboxOptions;

        public PaymentOperationsConsumer(
            ILogger<PaymentOperationsConsumer> logger,
            IDateTimeProvider dateTimeProvider,
            IEventStoreDbWriteClient<PaymentOperationEvent> eventStoreDbWriteClient,
            IOptions<KafkaOptions> kafkaOptions,
            IOptions<PaymentInboxOptions> paymentInboxOptions,
            IServiceScopeFactory serviceScopeFactory)
            : base(EnrichConsumerOptions(kafkaOptions.Value), logger)
        {
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            _eventStoreDbWriteClient = eventStoreDbWriteClient;
            _paymentInboxOptions = paymentInboxOptions?.Value ?? new PaymentInboxOptions();
            _serviceScopeFactory = serviceScopeFactory;
        }

        internal PaymentOperationsConsumer(
            ILogger<PaymentOperationsConsumer> logger,
            IDateTimeProvider dateTimeProvider,
            IEventStoreDbWriteClient<PaymentOperationEvent> eventStoreDbWriteClient,
            IOptions<KafkaOptions> kafkaOptions,
            IOptions<PaymentInboxOptions> paymentInboxOptions,
            IPaymentMessageInboxService messageInboxService,
            IConsumer<string, string> consumer)
            : base(EnrichConsumerOptions(kafkaOptions.Value), logger, consumer)
        {
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            _eventStoreDbWriteClient = eventStoreDbWriteClient;
            _paymentInboxOptions = paymentInboxOptions?.Value ?? new PaymentInboxOptions();
            _messageInboxService = messageInboxService;
        }

        private static KafkaOptions EnrichConsumerOptions(KafkaOptions options)
        {
            var consumerSettings = options.ConsumerSettings;

            if (consumerSettings == null)
            {
                return options;
            }

            consumerSettings.GroupId = paymentsConsumerGroup;
            consumerSettings.ClientId = $"{paymentsConsumerGroup}-{Environment.GetEnvironmentVariable("HOSTNAME")}";

            consumerSettings.EnableAutoCommit = false;

            return options;
        }

        public override Task ConsumeAsync(CancellationToken cancellationToken)
        {
            return ConsumeAsync(
                payload => ProcessMessageAsync(payload, cancellationToken),
                cancellationToken);
        }

        private async Task ProcessMessageAsync(ConsumeResult<string, string> payload, CancellationToken cancellationToken)
        {
            var message = payload.Message;

            if (message == null || string.IsNullOrWhiteSpace(message.Value))
            {
                return;
            }

            if (_messageInboxService is not null)
            {
                await ProcessMessageAsync(payload, _messageInboxService, cancellationToken);
                return;
            }

            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var inboxService = scope.ServiceProvider.GetRequiredService<IPaymentMessageInboxService>();

            await ProcessMessageAsync(payload, inboxService, cancellationToken);
        }

        private async Task ProcessMessageAsync(
            ConsumeResult<string, string> payload,
            IPaymentMessageInboxService inboxService,
            CancellationToken cancellationToken)
        {
            var message = payload.Message;
            var nowUtc = _dateTimeProvider.GetNowUtc();
            var messageId = GetMessageId(payload);

            var processingDecision = await inboxService.StartProcessingAsync(new PaymentInboxMessageEntity
            {
                MessageId = messageId,
                Topic = payload.Topic ?? string.Empty,
                Partition = payload.Partition.Value,
                Offset = payload.Offset.Value,
                Status = PaymentInboxStatus.Processing,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc,
                RawMessage = message.Value
            });

            if (!processingDecision.ShouldProcess)
            {
                _logger.DuplicateMessageSkipped(messageId, processingDecision.Status);
                return;
            }

            message.Headers.Add(
                KafkaMessageHeaders.ProcessedAt,
                Encoding.UTF8.GetBytes(nowUtc.ToString("O")));

            _logger.PaymentConsumed(messageId);

            PaymentOperationEvent paymentEvent;

            try
            {
                paymentEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(message.Value);
            }
            catch (JsonException ex)
            {
                _logger.DeserializationFailed(message.Value, ex.Message, ex);
                await SendPoisonMessageToDeadLetterQueueAsync(payload, ex, cancellationToken);
                await inboxService.MarkPoisonAsync(messageId, ex.Message, _dateTimeProvider.GetNowUtc());
                return;
            }

            if (paymentEvent?.Payload == null)
            {
                var exception = new JsonException("Payment operation event or payload is empty.");
                _logger.DeserializationFailed(message.Value, exception.Message, exception);
                await SendPoisonMessageToDeadLetterQueueAsync(payload, exception, cancellationToken);
                await inboxService.MarkPoisonAsync(messageId, exception.Message, _dateTimeProvider.GetNowUtc());
                return;
            }

            var correlationId = paymentEvent.Metadata.Get(EventMetadataKeys.CorrelationId) ?? string.Empty;

            var traceParent = message.Headers.TryGetLastBytes(KafkaMessageHeaders.Traceparent, out var tpBytes)
                ? Encoding.UTF8.GetString(tpBytes)
                : null;

            var traceState = message.Headers.TryGetLastBytes(KafkaMessageHeaders.Tracestate, out var tsBytes)
                ? Encoding.UTF8.GetString(tsBytes)
                : null;

            var baggage = message.Headers.TryGetLastBytes(KafkaMessageHeaders.Baggage, out var baggageBytes)
                ? Encoding.UTF8.GetString(baggageBytes)
                : null;

            var traceId = message.Headers.TryGetLastBytes(KafkaMessageHeaders.TraceId, out var tidBytes)
                ? Encoding.UTF8.GetString(tidBytes)
                : null;

            var causationId = message.Headers.TryGetLastBytes(KafkaMessageHeaders.CausationId, out var causationIdBytes)
                ? Encoding.UTF8.GetString(causationIdBytes)
                : null;

            var importBatchId = message.Headers.TryGetLastBytes(KafkaMessageHeaders.ImportBatchId, out var importBatchIdBytes)
                ? Encoding.UTF8.GetString(importBatchIdBytes)
                : null;

            var sourceSystem = message.Headers.TryGetLastBytes(KafkaMessageHeaders.Source, out var sourceSystemBytes)
                ? Encoding.UTF8.GetString(sourceSystemBytes)
                : null;

            using var activity = ActivityPropagation.StartActivity(
                "payment.events.eventstore.process",
                ActivityKind.Consumer,
                traceParent,
                traceState);

            if (activity != null)
            {
                activity.SetTag(ActivityTags.MessagingSystem, "kafka");
                activity.SetTag(ActivityTags.KafkaTopic, payload.Topic);
                activity.SetTag(ActivityTags.MessagingOperation, "process");
                activity.SetTag("messaging.kafka.partition", payload.Partition);
                activity.SetTag("messaging.kafka.offset", payload.Offset);
                activity.SetTag("messaging.message_id", messageId);
                activity.SetTag("messaging.conversation_id", causationId);
                activity.SetCorrelationId(correlationId);
            }

            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = correlationId;
            if (!string.IsNullOrEmpty(traceId))
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceId] = traceId;
            }

            if (!string.IsNullOrEmpty(traceParent))
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceParent] = traceParent;
            }

            if (!string.IsNullOrEmpty(traceState))
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceState] = traceState;
            }

            if (!string.IsNullOrEmpty(baggage))
            {
                paymentEvent.Metadata[EventMetadataKeys.Baggage] = baggage;
            }

            if (!string.IsNullOrEmpty(messageId))
            {
                paymentEvent.Metadata[EventMetadataKeys.MessageId] = messageId;
            }

            if (!string.IsNullOrEmpty(causationId))
            {
                paymentEvent.Metadata[EventMetadataKeys.CausationId] = causationId;
            }

            if (!string.IsNullOrEmpty(importBatchId))
            {
                paymentEvent.Metadata[EventMetadataKeys.ImportBatchId] = importBatchId;
            }

            if (!string.IsNullOrEmpty(sourceSystem))
            {
                paymentEvent.Metadata[EventMetadataKeys.SourceSystem] = sourceSystem;
            }

            activity?.AddEvent(ActivityEvents.KafkaConsumed);

            StampTraceMetadata(paymentEvent, activity);

            var streamName = GenerateStreamName(paymentEvent);
            var eventTypeTitle = $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";

            try
            {
                await _eventStoreDbWriteClient.SendBatchAsync(
                    [paymentEvent],
                    streamName,
                    eventTypeTitle,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                var failure = await inboxService.MarkFailedAsync(
                    messageId,
                    ex.Message,
                    Math.Max(1, _paymentInboxOptions.MaxRetryAttempts),
                    _dateTimeProvider.GetNowUtc());

                if (!failure.IsPoison)
                {
                    _logger.TransientMessageFailure(messageId, failure.RetryCount, ex.Message, ex);
                    throw;
                }

                _logger.PoisonMessageReachedDeadLetter(messageId, failure.RetryCount, ex.Message, ex);
                await SendPoisonMessageToDeadLetterQueueAsync(payload, ex, cancellationToken);
                return;
            }

            await inboxService.MarkProcessedAsync(messageId, _dateTimeProvider.GetNowUtc());
        }

        private async Task SendPoisonMessageToDeadLetterQueueAsync(
            ConsumeResult<string, string> payload,
            Exception exception,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = payload.Message;
            var deadLetterEvent = new PaymentOperationEvent
            {
                Payload = new FinancialTransaction
                {
                    Key = Guid.Empty,
                    PaymentAccountId = Guid.Empty
                },
                EventType = PaymentEventTypes.Removed
            };

            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.CorrelationId, deadLetterEvent, EventMetadataKeys.CorrelationId);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.TraceId, deadLetterEvent, EventMetadataKeys.TraceId);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.Traceparent, deadLetterEvent, EventMetadataKeys.TraceParent);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.Tracestate, deadLetterEvent, EventMetadataKeys.TraceState);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.Baggage, deadLetterEvent, EventMetadataKeys.Baggage);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.MessageId, deadLetterEvent, EventMetadataKeys.MessageId);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.CausationId, deadLetterEvent, EventMetadataKeys.CausationId);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.ImportBatchId, deadLetterEvent, EventMetadataKeys.ImportBatchId);
            CopyHeaderToMetadata(message.Headers, KafkaMessageHeaders.Source, deadLetterEvent, EventMetadataKeys.SourceSystem);

            deadLetterEvent.Metadata[EventMetadataKeys.FromMessage] = message.Key ?? string.Empty;
            deadLetterEvent.Metadata["kafka-topic"] = payload.Topic ?? string.Empty;
            deadLetterEvent.Metadata["kafka-partition"] = payload.Partition.Value.ToString(CultureInfo.InvariantCulture);
            deadLetterEvent.Metadata["kafka-offset"] = payload.Offset.Value.ToString(CultureInfo.InvariantCulture);
            deadLetterEvent.Metadata["raw-message"] = message.Value ?? string.Empty;

            await _eventStoreDbWriteClient.SendToDeadLetterQueueAsync(deadLetterEvent, exception);
        }

        private static void CopyHeaderToMetadata(
            Headers headers,
            string headerName,
            PaymentOperationEvent paymentEvent,
            string metadataName)
        {
            if (headers.TryGetLastBytes(headerName, out var bytes))
            {
                paymentEvent.Metadata[metadataName] = Encoding.UTF8.GetString(bytes);
            }
        }

        private static string GetMessageId(ConsumeResult<string, string> payload)
        {
            var message = payload.Message;
            if (message.Headers.TryGetLastBytes(KafkaMessageHeaders.MessageId, out var messageIdBytes))
            {
                var messageId = Encoding.UTF8.GetString(messageIdBytes);
                if (!string.IsNullOrWhiteSpace(messageId))
                {
                    return messageId;
                }
            }

            if (!string.IsNullOrWhiteSpace(message.Key))
            {
                return message.Key;
            }

            return $"{payload.Topic}:{payload.Partition.Value}:{payload.Offset.Value}";
        }

        private static string GenerateStreamName(PaymentOperationEvent paymentEvent)
        {
            var accountPerMonthIdentifier = paymentEvent.Payload.GetMonthPeriodPaymentAccountIdentifier();
            return PaymentOperationNamesGenerator.GenerateForAccountMonthStream(accountPerMonthIdentifier);
        }

        private static void StampTraceMetadata(PaymentOperationEvent paymentEvent, Activity activity)
        {
            if (activity is null || paymentEvent is null)
            {
                return;
            }

            var propagationCarrier = TraceContextPropagation.Capture(activity);

            paymentEvent.Metadata[EventMetadataKeys.TraceId] = activity.TraceId.ToString();
            paymentEvent.Metadata[EventMetadataKeys.CausationId] = activity.SpanId.ToString();

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
            else
            {
                paymentEvent.Metadata.Remove(EventMetadataKeys.Baggage);
            }
        }
    }
}
