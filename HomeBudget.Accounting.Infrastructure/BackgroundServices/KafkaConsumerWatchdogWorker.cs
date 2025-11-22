using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Helpers;
using HomeBudget.Accounting.Infrastructure.Logs;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaConsumerWatchdogWorker(
        ILogger<KafkaConsumerWatchdogWorker> logger,
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
                await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerHealthCheckIntervalSeconds), stoppingToken);

                try
                {
                    logger.RunningKafkaConsumerHealthCheck();

                    var topicsWithLag = topicProcessor.GetTopicsWithLag(stoppingToken);

                    await HandleTopicsWithLagAsync(topicsWithLag, consumerSettings.GroupId, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.ErrorConsumerMonitoringMessages(ex);
                    await Task.Delay(TimeSpan.FromSeconds(consumerSettings.ConsumerHealthCheckIntervalSeconds), stoppingToken);
                }
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

        private async Task HandleTopicsWithLagAsync(IEnumerable<SubscriptionTopic> topicsWithLag, string groupId, CancellationToken stoppingToken)
        {
            foreach (var topic in topicsWithLag)
            {
                if (await topicManager.HasActiveConsumerAsync(topic.Title, groupId))
                {
                    continue;
                }

                // Resubscribe(topic);
            }
        }
    }
}
