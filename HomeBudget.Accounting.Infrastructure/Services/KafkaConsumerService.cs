using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Helpers;
using HomeBudget.Accounting.Infrastructure.Logs;
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
                KafkaConsumerServiceLogs.ConsumeLoopStopped(logger);
                return;
            }

            while (true)
            {
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    if (ConsumersStore.Consumers.IsEmpty)
                    {
                        KafkaConsumerServiceLogs.NoActiveConsumers(logger);
                        await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                        continue;
                    }

                    var consumersWithSubscriptions = ConsumersStore.Consumers.Values
                        .Where(c => !c.Any(s => s.Subscriptions.IsNullOrEmpty()))
                        .ToList();

                    foreach (var consumer in consumersWithSubscriptions.SelectMany(c => c))
                    {
                        _ = Task.Run(async () => await consumer.ConsumeAsync(stoppingToken), stoppingToken);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    KafkaConsumerServiceLogs.ConsumeLoopCancelled(logger);
                    break;
                }
                catch (Exception ex)
                {
                    KafkaConsumerServiceLogs.ErrorConsumingMessages(logger, ex);
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
                if (!ConsumersStore.Consumers.TryGetValue(topicTitle, out var topicConsumers))
                {
                    ConsumersStore.Consumers.TryAdd(topicTitle, [consumer]);
                }
                else
                {
                    topicConsumers.Add(consumer);
                }

                consumer.Subscribe(topicTitle);
                KafkaConsumerServiceLogs.SubscribedToTopic(logger, topicTitle, topic.ConsumerType.ToString());
            }

            return consumer;
        }
    }
}
