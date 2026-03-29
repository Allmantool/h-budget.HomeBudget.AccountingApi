using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Consumers
{
    internal class PaymentOperationsConsumer(
        ILogger<PaymentOperationsConsumer> logger,
        IDateTimeProvider dateTimeProvider,
        IServiceScopeFactory scopeFactory,
        Channel<PaymentOperationEvent> paymentEventsChannel,
        IOptions<KafkaOptions> kafkaOptions)
        : BaseKafkaConsumer<string, string>(EnrichConsumerOptions(kafkaOptions.Value), logger)
    {
        private static string paymentsConsumerGroup = "accounting.payments.group";

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
                async payload =>
                {
                    try
                    {
                        var message = payload.Message;

                        if (message == null || string.IsNullOrWhiteSpace(message.Value))
                        {
                            return;
                        }

                        message.Headers.Add(
                            KafkaMessageHeaders.ProcessedAt,
                            Encoding.UTF8.GetBytes(dateTimeProvider.GetNowUtc().ToString("O")));

                        logger.PaymentConsumed(message.Key);

                        var paymentEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(message.Value);

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

                        var messageId = message.Headers.TryGetLastBytes(KafkaMessageHeaders.MessageId, out var messageIdBytes)
                            ? Encoding.UTF8.GetString(messageIdBytes)
                            : null;

                        var causationId = message.Headers.TryGetLastBytes(KafkaMessageHeaders.CausationId, out var causationIdBytes)
                            ? Encoding.UTF8.GetString(causationIdBytes)
                            : null;

                        using var activity = ActivityPropagation.StartActivity(
                            "payment.events.channel.process",
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
                            activity.SetTag("messaging.message_id", messageId ?? message.Key);
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

                        var payloadData = paymentEvent.Payload;
                        var partitionKey = payloadData.GetPartitionKey();

                        using var scope = scopeFactory.CreateScope();

                        var outboxPaymentStatusService = scope.ServiceProvider
                            .GetRequiredService<IOutboxPaymentStatusService>();

                        outboxPaymentStatusService.SetStatus(partitionKey, OutboxStatus.Published);
                        activity?.AddEvent(ActivityEvents.KafkaConsumed);

                        await paymentEventsChannel.Writer.WriteAsync(paymentEvent);
                    }
                    catch (JsonException ex)
                    {
                        logger.DeserializationFailed(payload.Message?.Value, ex.Message, ex);
                    }
                    catch (Exception ex)
                    {
                        logger.ConsumerFailed(nameof(PaymentOperationsConsumer), ex.Message, ex);
                    }
                },
                async payload =>
                {
                    var message = payload.Message;
                    if (message == null || string.IsNullOrWhiteSpace(message.Value))
                    {
                        return;
                    }

                    var traceParent = message.Headers.TryGetLastBytes("traceparent", out var tpBytes)
                        ? Encoding.UTF8.GetString(tpBytes)
                        : null;

                    var traceState = message.Headers.TryGetLastBytes(KafkaMessageHeaders.Tracestate, out var tsBytes)
                        ? Encoding.UTF8.GetString(tsBytes)
                        : null;

                    message.Headers.Add(
                        KafkaMessageHeaders.ProcessedAt,
                        Encoding.UTF8.GetBytes(dateTimeProvider.GetNowUtc().ToString("O")));

                    using var activity = ActivityPropagation.StartActivity(
                        "outbox.status.acknowledged",
                        ActivityKind.Consumer,
                        traceParent,
                        traceState);

                    logger.PaymentConsumed(message.Key);

                    var paymentEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(message.Value);
                    var payloadData = paymentEvent.Payload;
                    var partitionKey = payloadData.GetPartitionKey();

                    using var scope = scopeFactory.CreateScope();

                    var outboxPaymentStatusService = scope.ServiceProvider
                        .GetRequiredService<IOutboxPaymentStatusService>();

                    outboxPaymentStatusService.SetStatus(partitionKey, OutboxStatus.Acknowledged);
                    activity?.AddEvent(ActivityEvents.OutboxAcknowledged);
                },
                cancellationToken);
        }
    }
}
