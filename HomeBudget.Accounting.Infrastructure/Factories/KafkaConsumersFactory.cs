using System;
using System.ComponentModel;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    internal class KafkaConsumersFactory
        : IKafkaConsumersFactory
    {
        private IServiceProvider _serviceProvider;

        public IKafkaConsumer Build(string consumerType)
        {
            if (_serviceProvider == null)
            {
                throw new InvalidEnumArgumentException($"Pls. provide {nameof(IServiceProvider)}");
            }

            var consumers = _serviceProvider.GetServices<IKafkaConsumer>();

            return consumers.FirstOrDefault(c => string.Equals(c.GetType().Name, consumerType, StringComparison.OrdinalIgnoreCase));
        }

        public IKafkaConsumersFactory WithServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            return this;
        }
    }
}
