using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
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
        public async Task ConsumeKafkaMessagesLoopAsync(
            IKafkaConsumer consumer,
            CancellationToken stoppingToken)
        {
            logger.ConsumeLoopStarted();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await consumer.ConsumeAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    logger.ConsumeLoopCancelled();
                    throw;
                }
                catch (ConsumeException ex) when (!ex.Error.IsFatal)
                {
                    logger.NonFatalConsumeError(ex);
                }
                catch (KafkaException ex)
                {
                    logger.ErrorConsumingMessages(ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.ErrorConsumingMessages(ex);
                    throw;
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
