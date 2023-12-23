using System;
using System.Threading.Tasks;

using Confluent.Kafka;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IKafkaDependentProducer<K, V>
    {
        Task ProduceAsync(string topic, Message<K, V> message);

        void Produce(
            string topic,
            Message<K, V> message,
            Action<DeliveryReport<K, V>> deliveryHandler = null
        );

        void Flush(TimeSpan timeout);
    }
}
