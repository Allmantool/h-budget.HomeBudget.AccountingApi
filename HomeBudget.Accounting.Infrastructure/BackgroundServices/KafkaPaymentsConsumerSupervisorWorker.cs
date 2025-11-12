using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Helpers;
using HomeBudget.Accounting.Infrastructure.Logs;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaPaymentsConsumerSupervisorWorker(
        ILogger<KafkaPaymentsConsumerSupervisorWorker> logger,
        IOptions<KafkaOptions> options,
        ITopicManager topicManager,
        IConsumerService consumerService)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumerSettings = options.Value.ConsumerSettings;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var activeConsumersAmount = GetAlivePaymentConsumersAmount();

                    if (activeConsumersAmount <= consumerSettings.MaxAccountingPaymentConsumers && topicManager.IsBrokerReady())
                    {
                        var topic = new SubscriptionTopic
                        {
                            ConsumerType = ConsumerTypes.PaymentOperations,
                            Title = BaseTopics.AccountingPayments
                        };

                        consumerService.CreateAndSubscribe(topic);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    KafkaPaymentsConsumerBackgroundServiceLogs.OperationCanceled(
                        logger,
                        nameof(KafkaPaymentsConsumerSupervisorWorker),
                        ex);
                }
                catch (Exception ex)
                {
                    KafkaPaymentsConsumerBackgroundServiceLogs.UnexpectedError(
                        logger,
                        nameof(KafkaPaymentsConsumerSupervisorWorker),
                        consumerSettings.ConsumerCircuitBreakerDelayInSeconds,
                        ex);

                    await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }
        }

        private static int GetAlivePaymentConsumersAmount()
        {
            if (ConsumersStore.Consumers.TryGetValue(BaseTopics.AccountingPayments, out var paymentConsumers))
            {
                var activeConsumers = paymentConsumers.Where(p => p.IsAlive()).ToList();

                return activeConsumers.Count;
            }

            return 0;
        }
    }
}
