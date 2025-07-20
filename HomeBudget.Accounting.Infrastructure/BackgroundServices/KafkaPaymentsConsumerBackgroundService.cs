using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Exceptions;
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
                    if (ConsumersStore.Consumers.TryGetValue(BaseTopics.AccountingPayments, out var consumers))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                        continue;
                    }

                    if (!consumers.IsNullOrEmpty() && consumers.Count() >= consumerSettings.MaxAccountingPaymentConsumers)
                    {
                        continue;
                    }

                    foreach (var _ in Enumerable.Range(1, consumerSettings.MaxAccountingPaymentConsumers))
                    {
                        consumerService.CreateAndSubscribe(new SubscriptionTopic
                        {
                            ConsumerType = ConsumerTypes.PaymentOperations,
                            Title = BaseTopics.AccountingPayments
                        });
                    }
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogError(ex, "Shutting down {Service}...", nameof(KafkaPaymentsConsumerBackgroundService));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                    ex,
                    "Unexpected error in {Service}. Restarting in {Delay} seconds...",
                    nameof(KafkaPaymentsConsumerBackgroundService),
                    options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds);

                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }
        }
    }
}
