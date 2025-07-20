using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaConsumerHealthMonitorBackgroundService(
        ILogger<KafkaConsumerHealthMonitorBackgroundService> logger,
        IOptions<KafkaOptions> options,
        IConsumerService consumerService,
        ITopicProcessor topicProcessor,
        ITopicManager topicManager)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var kafkaOptions = options.Value;
            var consumerSettings = kafkaOptions.ConsumerSettings;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    logger.LogInformation("Running Kafka consumer health check...");

                    var topicsWithLag = topicProcessor.GetTopicsWithLag(stoppingToken);

                    foreach (var topic in topicsWithLag)
                    {
                        var hasActiveConsumer = await topicManager.HasActiveConsumerAsync(topic.Title, consumerSettings.GroupId);

                        if (hasActiveConsumer)
                        {
                            continue;
                        }

                        // Resubscribe(topic);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during Kafka consumer health check. Retrying after delay...");
                }

                await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerHealthCheckIntervalSeconds), stoppingToken);
            }
        }

        public void Resubscribe(SubscriptionTopic topic)
        {
            var topicTitle = topic.Title;

            logger.LogWarning("No active consumer for topic '{Topic}'. Attempting to (re)create one.", topicTitle);

            if (ConsumersStore.Consumers.TryRemove(topicTitle, out var consumersForRemove))
            {
                foreach (var consumer in consumersForRemove)
                {
                    consumer.UnSubscribe();
                }
            }

            consumerService.CreateAndSubscribe(topic);
        }
    }
}
