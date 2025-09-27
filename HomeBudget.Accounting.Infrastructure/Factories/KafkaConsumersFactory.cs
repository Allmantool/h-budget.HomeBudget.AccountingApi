using System;
using System.ComponentModel;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    internal class KafkaConsumersFactory(IServiceProvider serviceProvider) : IKafkaConsumersFactory
    {
        private string _topicToSubscribe;

        public IKafkaConsumersFactory WithTopic(string topicTitle)
        {
            _topicToSubscribe = topicTitle;

            return this;
        }

        public IKafkaConsumer Build(string consumerType)
        {
            if (serviceProvider == null)
            {
                throw new InvalidEnumArgumentException($"Pls. provide {nameof(IServiceProvider)}");
            }

            var consumers = serviceProvider.GetServices<BaseKafkaConsumer<string, string>>();

            return consumers.FirstOrDefault(c => string.Equals(c.GetType().Name, consumerType, StringComparison.OrdinalIgnoreCase));
        }
    }
}
