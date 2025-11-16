using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer
{
    internal class KafkaPaymentsConsumerSupervisorWorker(
        ILogger<KafkaPaymentsConsumerSupervisorWorker> logger,
        IOptions<KafkaOptions> options,
        ITopicManager topicManager,
        IConsumerService consumerService)
        : BackgroundService
    {
        private IKafkaConsumer _consumer;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumerSettings = options.Value.ConsumerSettings;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (topicManager.IsBrokerReady() && _consumer is null)
                    {
                        var topic = new SubscriptionTopic
                        {
                            ConsumerType = ConsumerTypes.PaymentOperations,
                            Title = BaseTopics.AccountingPayments
                        };

                        _consumer = consumerService.CreateAndSubscribe(topic);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                    }

                    if (_consumer is not null || _consumer.IsAlive())
                    {
                        await consumerService.ConsumeKafkaMessagesLoopAsync(_consumer, stoppingToken);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    logger.OperationCanceled(
                        nameof(KafkaPaymentsConsumerSupervisorWorker),
                        ex);
                }
                catch (Exception ex)
                {
                    logger.UnexpectedError(
                        nameof(KafkaPaymentsConsumerSupervisorWorker),
                        consumerSettings.ConsumerCircuitBreakerDelayInSeconds,
                        ex);

                    await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _consumer?.UnSubscribe();
                _consumer?.Dispose();
            }
            catch (Exception ex)
            {
                logger.FailedToDisposeConsumer(ex);
            }

            return base.StopAsync(cancellationToken);
        }
    }
}
