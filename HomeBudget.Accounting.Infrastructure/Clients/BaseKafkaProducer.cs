using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public class BaseKafkaProducer<TKey, TValue>(IKafkaClientHandler handle)
        : IKafkaProducer<TKey, TValue>
    {
        private readonly IProducer<TKey, TValue> _kafkaHandle = new DependentProducerBuilder<TKey, TValue>(handle.Handle).Build();

        public void Produce(
            string topic,
            Message<TKey, TValue> message,
            Action<DeliveryReport<TKey, TValue>> deliveryHandler = null)
            => _kafkaHandle.Produce(topic, message, deliveryHandler);

        public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
            string topic,
            Message<TKey, TValue> message,
            CancellationToken token)
            => _kafkaHandle.ProduceAsync(topic, message, token);

        public void Flush(TimeSpan timeout) => _kafkaHandle.Flush(timeout);
    }
}
