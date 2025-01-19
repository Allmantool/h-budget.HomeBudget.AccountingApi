using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Core.Options;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Consumers
{
    internal class PaymentOperationsConsumer(
        ILogger<PaymentOperationsConsumer> logger,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
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

            consumerSettings.ClientId = ConsumerTypes.PaymentOperations;
            consumerSettings.GroupId = "payment-account-operations";
            consumerSettings.EnableAutoCommit = true;

            return options;
        }

        public override async Task ConsumeAsync(CancellationToken cancellationToken)
        {
            await ConsumeAsync(
                async payload =>
                {
                    try
                    {
                        var message = payload.Message;

                        var paymentEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(message.Value);

                        await operationsDeliveryHandler.HandleAsync(paymentEvent, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("{PaymentOperationsConsumer} failed to consume with error: {Exception}", nameof(PaymentOperationsConsumer), ex.Message);
                    }
                },
                cancellationToken);
        }
    }
}
