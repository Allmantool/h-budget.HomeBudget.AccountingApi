using Confluent.Kafka;
using HomeBudget.Accounting.Domain.Models;

using HomeBudget.Accounting.Infrastructure.Clients;
using Microsoft.Extensions.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsClientHandlerHandler : IKafkaClientHandler
    {
        private IProducer<byte[], byte[]> KafkaProducer { get; }

        public PaymentOperationsClientHandlerHandler(IOptions<KafkaOptions> options)
        {
            var kafkaOptions = options.Value;
            var producerSettings = kafkaOptions.ProducerSettings;

            var conf = new ProducerConfig(new ProducerConfig
            {
                BootstrapServers = producerSettings.BootstrapServers,
            });
            KafkaProducer = new ProducerBuilder<byte[], byte[]>(conf).Build();
        }

        public Handle Handle => KafkaProducer.Handle;

        public void Dispose()
        {
            KafkaProducer.Flush();
            KafkaProducer.Dispose();
        }
    }
}
