using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Services
{
    internal class KafkaConsumerService(
        ILogger<KafkaConsumerService> logger,
        IOptions<KafkaOptions> options,
        IKafkaConsumersFactory kafkaConsumersFactory)
        : IConsumerService
    {
        public async Task ConsumeKafkaMessagesLoopAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Consume loop has been stopped");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (ConsumersStore.Consumers.IsEmpty)
                    {
                        logger.LogTrace("No active consumers. Waiting for new topics...");
                        await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                        continue;
                    }

                    var consumersWithSubscriptions = ConsumersStore.Consumers.Values.Where(c => !c.Subscriptions.IsNullOrEmpty());

                    var consumeTasks = consumersWithSubscriptions.Select(c => c.ConsumeAsync(stoppingToken));
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await Task.WhenAll(consumeTasks);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "One or more consumer tasks failed inside Task.Run.");
                            }
                        }, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error while consuming Kafka messages.");
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                }
            }
        }

        public IKafkaConsumer CreateAndSubscribe(SubscriptionTopic topic)
        {
            var consumer = kafkaConsumersFactory
                .WithTopic(topic.Title)
                .Build(topic.ConsumerType);

            if (ConsumersStore.Consumers.TryAdd(topic.Title, consumer))
            {
                consumer.Subscribe(topic.Title);
                logger.LogInformation("Subscribed to topic {Title}, consumer type {ConsumerType}", topic.Title, topic.ConsumerType);
            }

            return consumer;
        }
    }
}
