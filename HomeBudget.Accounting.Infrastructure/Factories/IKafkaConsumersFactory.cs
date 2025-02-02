using System;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    internal interface IKafkaConsumersFactory
    {
        IKafkaConsumer Build(string consumerType);
        IKafkaConsumersFactory WithServiceProvider(IServiceProvider sp);
    }
}
