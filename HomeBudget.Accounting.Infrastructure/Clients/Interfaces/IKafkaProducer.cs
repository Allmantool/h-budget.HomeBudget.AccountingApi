using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IKafkaProducer<TKey, TValue>
    {
        void Produce(
            string topic,
            Message<TKey, TValue> message,
            Action<DeliveryReport<TKey, TValue>> deliveryHandler = null
        );

        Task<DeliveryResult<TKey, TValue>> ProduceAsync(
            string topic,
            Message<TKey, TValue> message,
            CancellationToken token);

        void Flush(TimeSpan timeout);
    }
}
