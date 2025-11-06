using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Helpers;
using HomeBudget.Accounting.Infrastructure.Logs;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaPaymentsConsumerBackgroundService(
        ILogger<KafkaPaymentsConsumerBackgroundService> logger,
        IOptions<KafkaOptions> options,
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
                    if (GetAlivePaymentConsumers().Count() >= consumerSettings.MaxAccountingPaymentConsumers)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                        continue;
                    }

                    consumerService.CreateAndSubscribe(new SubscriptionTopic
                    {
                        ConsumerType = ConsumerTypes.PaymentOperations,
                        Title = BaseTopics.AccountingPayments
                    });
                }
                catch (OperationCanceledException ex)
                {
                    KafkaPaymentsConsumerBackgroundServiceLogs.OperationCanceled(
                        logger,
                        nameof(KafkaPaymentsConsumerBackgroundService),
                        ex);
                }
                catch (Exception ex)
                {
                    KafkaPaymentsConsumerBackgroundServiceLogs.UnexpectedError(
                        logger,
                        nameof(KafkaPaymentsConsumerBackgroundService),
                        consumerSettings.ConsumerCircuitBreakerDelayInSeconds,
                        ex);

                    await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }
        }

        private static IEnumerable<IKafkaConsumer> GetAlivePaymentConsumers()
        {
            if (ConsumersStore.Consumers.TryGetValue(BaseTopics.AccountingPayments, out var paymentConsumers))
            {
                return paymentConsumers.Where(p => p.IsAlive());
            }

            return Enumerable.Empty<BaseKafkaConsumer<string, string>>();
        }
    }
}
