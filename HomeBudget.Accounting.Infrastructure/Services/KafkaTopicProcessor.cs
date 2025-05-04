using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services
{
    internal sealed class KafkaTopicProcessor(
        ILogger<KafkaTopicProcessor> logger,
        ITopicManager topicManager,
        IConsumerService consumerService)
    : ITopicProcessor
    {
        public IEnumerable<SubscriptionTopic> GetTopicsWithLag(CancellationToken token)
        {
            var topics = topicManager.GetAll();

            foreach (var topic in topics)
            {
                token.ThrowIfCancellationRequested();

                var lag = topicManager.GetTopicLag(topic);

                if (lag > 0)
                {
                    yield return new SubscriptionTopic
                    {
                        Title = topic,
                        ConsumerType = ConsumerTypes.PaymentOperations
                    };
                }
            }
        }

        public async Task EnsureProcessingAsync(SubscriptionTopic topic, CancellationToken token)
        {
            try
            {
                await topicManager.CreateAsync(topic.Title, token);

                if (ConsumersStore.Consumers.ContainsKey(topic.Title))
                {
                    logger.LogWarning("Topic '{Topic}' is already active.", topic.Title);
                    return;
                }

                consumerService.CreateAndSubscribe(topic);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process topic '{Topic}': {Message}", topic.Title, ex.Message);
            }
        }
    }
}
