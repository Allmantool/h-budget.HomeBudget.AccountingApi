using System;
using System.Collections.Generic;
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

            while (true)
            {
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    if (ConsumersStore.Consumers.IsEmpty)
                    {
                        logger.LogTrace("No active consumers. Waiting for new topics...");
                        await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                        continue;
                    }

                    var consumersWithSubscriptions = ConsumersStore.Consumers.Values
                        .Where(c => !c.Any(s => s.Subscriptions.IsNullOrEmpty()))
                        .ToList();

                    List<Task> consumeTasks = [];
                    foreach (var consumers in consumersWithSubscriptions)
                    {
                        if (consumers.IsNullOrEmpty())
                        {
                            continue;
                        }

                        var tasks = consumers.Select(c => c.ConsumeAsync(stoppingToken)).ToList();

                        consumeTasks.AddRange(consumers.Select(c => c.ConsumeAsync(stoppingToken)));
                    }

                    if (consumeTasks.IsNullOrEmpty())
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                        continue;
                    }

                    await Task.WhenAll(consumeTasks);
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Consume loop has been cancelled.");
                    break;
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
            var topicTitle = topic.Title;

            var consumer = kafkaConsumersFactory
                .WithTopic(topicTitle)
                .Build(topic.ConsumerType);

            if (consumer != null)
            {
                ConsumersStore.Consumers.TryGetValue(topicTitle, out var topicConsumers);

                var consumers = topicConsumers.IsNullOrEmpty()
                        ? [consumer]
                        : topicConsumers.Append(consumer);

                ConsumersStore.Consumers.Remove(topicTitle, out var _);
                ConsumersStore.Consumers.TryAdd(topicTitle, consumers);

                consumer.Subscribe(topicTitle);
                logger.LogInformation("Subscribed to topic {Title}, consumer type {ConsumerType}", topicTitle, topic.ConsumerType);
            }

            return consumer;
        }
    }
}
