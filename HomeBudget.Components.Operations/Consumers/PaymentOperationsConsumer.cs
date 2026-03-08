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

                        var traceId = message.Headers.TryGetLastBytes(KafkaMessageHeaders.TraceId, out var tidBytes)
                            ? Encoding.UTF8.GetString(tidBytes)
                            : null;

                        using var activity = Telemetry.ActivitySource.StartActivity(
                            "payment.events.channel.process",
                            ActivityKind.Consumer,
                            traceParent);

                        if (activity != null)
                        {
                            activity.SetTag("messaging.system", "kafka");
                            activity.SetTag("messaging.destination", payload.Topic);
                            activity.SetTag("messaging.kafka.partition", payload.Partition);
                            activity.SetTag("messaging.kafka.offset", payload.Offset);
                            activity.SetTag("messaging.message_id", message.Key);
                            activity.SetCorrelationId(correlationId);

                            if (!string.IsNullOrWhiteSpace(traceId))
                            {
                                activity.SetTraceId(traceId);
                            }
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

                    message.Headers.Add(
                        KafkaMessageHeaders.ProcessedAt,
                        Encoding.UTF8.GetBytes(dateTimeProvider.GetNowUtc().ToString("O")));

                    using var activity = Telemetry.ActivitySource.StartActivity(
                        "outbox.status.acknowledged",
                        ActivityKind.Consumer,
                        traceParent);

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
