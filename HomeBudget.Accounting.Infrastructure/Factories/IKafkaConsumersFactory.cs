using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    public interface IKafkaConsumersFactory
    {
        IKafkaConsumer Build(string consumerType);
        IKafkaConsumersFactory WithTopic(string topicTitle);
    }
}
