using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IKafkaDependentProducer<K, V>
    {
        void Produce(
            string topic,
            Message<K, V> message,
            Action<DeliveryReport<K, V>> deliveryHandler = null
        );

        Task<DeliveryResult<K, V>> ProduceAsync(
            string topic,
            Message<K, V> message,
            CancellationToken token);

        void Flush(TimeSpan timeout);
    }
}
