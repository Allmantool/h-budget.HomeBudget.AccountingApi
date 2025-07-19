using System.Collections.Generic;
using System.Threading;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services
{
    internal sealed class KafkaTopicProcessor(ITopicManager topicManager)
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
    }
}
