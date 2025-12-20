using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
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
    internal class KafkaPaymentsConsumerWorker(
        ILogger<KafkaPaymentsConsumerWorker> logger,
        IOptions<KafkaOptions> options,
        ITopicManager topicManager,
        IConsumerService consumerService)
        : BackgroundService
    {
        private IKafkaConsumer _consumer;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var settings = options.Value.ConsumerSettings;
            var delay = TimeSpan.FromSeconds(settings.ConsumerCircuitBreakerDelayInSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_consumer is null)
                    {
                        logger.CreateKafkaConsumer();

                        var topic = new SubscriptionTopic
                        {
                            ConsumerType = ConsumerTypes.PaymentOperations,
                            Title = BaseTopics.AccountingPayments
                        };

                        _consumer = consumerService.CreateAndSubscribe(topic);
                    }

                    await consumerService.ConsumeKafkaMessagesLoopAsync(
                        _consumer,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (KafkaException ex)
                {
                    logger.RecreateConsumerAfterDelay(ex);

                    CleanupConsumer();
                    await Task.Delay(delay, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.RestartingConsumer(ex);

                    CleanupConsumer();
                    await Task.Delay(delay, stoppingToken);
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

        private void CleanupConsumer()
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
            finally
            {
                _consumer = null;
            }
        }
    }
}
