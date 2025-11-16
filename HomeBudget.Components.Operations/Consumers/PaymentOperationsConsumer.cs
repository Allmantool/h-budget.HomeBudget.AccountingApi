using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Consumers
{
    internal class PaymentOperationsConsumer(
        ILogger<PaymentOperationsConsumer> logger,
        Channel<PaymentOperationEvent> paymentEventsChannel,
        IOptions<KafkaOptions> kafkaOptions)
        : BaseKafkaConsumer<string, string>(EnrichConsumerOptions(kafkaOptions.Value), logger)
    {
        private static KafkaOptions EnrichConsumerOptions(KafkaOptions options)
        {
            var consumerSettings = options.ConsumerSettings;

            if (consumerSettings == null)
            {
                return options;
            }

            consumerSettings.GroupId = "accounting.payments.group";
            consumerSettings.ClientId = $"{consumerSettings.GroupId}_{Guid.NewGuid()}";

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

                        message.Headers.Add(KafkaMessageHeaders.ProcessedAt, Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));

                        PaymentOperationsConsumerLogs.PaymentConsumed(logger, message.Key);

                        var paymentEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(message.Value);

                        paymentEvent.Metadata.Add(EventMetadataKeys.FromMessage, message.Key);

                        await paymentEventsChannel.Writer.WriteAsync(paymentEvent);
                    }
                    catch (JsonException ex)
                    {
                        logger.DeserializationFailed(payload.Message.Value, ex.Message, ex);
                    }
                    catch (Exception ex)
                    {
                        logger.ConsumerFailed(nameof(PaymentOperationsConsumer), ex.Message, ex);
                    }
                },
                cancellationToken);
        }
    }
}
