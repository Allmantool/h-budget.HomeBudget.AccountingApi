using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Services
{
    internal class KafkaConsumerService(
        ILogger<KafkaConsumerService> logger,
        IOptions<KafkaOptions> options,
        IKafkaConsumersFactory kafkaConsumersFactory)
        : IConsumerService
    {
        public async Task ConsumeKafkaMessagesLoopAsync(IKafkaConsumer consumer, CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.ConsumeLoopStopped();
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    if (consumer is null || !consumer.IsAlive())
                    {
                        logger.NoActiveConsumers();
                        await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                        continue;
                    }

                    if (!consumer.Subscriptions.IsNullOrEmpty())
                    {
                        await consumer.ConsumeAsync(stoppingToken);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    logger.ConsumeLoopCancelled();
                    break;
                }
                catch (Exception ex)
                {
                    logger.ErrorConsumingMessages(ex);
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                }
            }
        }

        public IKafkaConsumer CreateAndSubscribe(SubscriptionTopic topic)
        {
            var topicTitle = topic.Title;

            var consumer = kafkaConsumersFactory
                .WithTopic(topicTitle)
                .Build(topic.ConsumerType);

            if (consumer is not null)
            {
                consumer.Subscribe(topicTitle);

                logger.SubscribedToTopic(topicTitle, topic.ConsumerType);
            }

            return consumer;
        }
    }
}
