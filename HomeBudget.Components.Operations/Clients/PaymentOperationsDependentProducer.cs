using System;
using System.Threading.Tasks;

using Confluent.Kafka;

using HomeBudget.Accounting.Infrastructure.Clients;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsDependentProducer<K, V>(IKafkaClientHandler handle)
        : IKafkaDependentProducer<K, V>
    {
        private readonly IProducer<K, V> _kafkaHandle = new DependentProducerBuilder<K, V>(handle.Handle).Build();

        public Task ProduceAsync(string topic, Message<K, V> message) =>
            _kafkaHandle.ProduceAsync(topic, message);

        public void Produce(
            string topic,
            Message<K, V> message,
            Action<DeliveryReport<K, V>> deliveryHandler = null)
            => _kafkaHandle.Produce(topic, message, deliveryHandler);

        public void Flush(TimeSpan timeout) => _kafkaHandle.Flush(timeout);
    }
}
