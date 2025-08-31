using HomeBudget.Accounting.Infrastructure.Consumers;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    internal interface IKafkaConsumersFactory
    {
        BaseKafkaConsumer<string, string> Build(string consumerType);
        IKafkaConsumersFactory WithTopic(string topicTitle);
    }
}
