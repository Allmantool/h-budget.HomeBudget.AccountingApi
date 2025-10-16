using System;

using Confluent.Kafka;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal sealed class PaymentOperationsClientHandler : IKafkaClientHandler
    {
        private IProducer<byte[], byte[]> KafkaProducer { get; }

        public PaymentOperationsClientHandler(IOptions<KafkaOptions> options)
        {
            var kafkaOptions = options.Value;

            var producerSettings = kafkaOptions.ProducerSettings;

            var producerConfig = new ProducerConfig
            {
                ClientId = "PaymentOperationsClientId",
                BootstrapServers = producerSettings.BootstrapServers,
                MessageTimeoutMs = producerSettings.MessageTimeoutMs ?? TimeSpan.FromSeconds(15).Microseconds,
                Acks = Acks.All
            };

            KafkaProducer = new ProducerBuilder<byte[], byte[]>(producerConfig).Build();
        }

        public Handle Handle => KafkaProducer.Handle;

        public void Dispose()
        {
            if (KafkaProducer != null)
            {
                KafkaProducer.Flush();
                KafkaProducer.Dispose();
            }
        }
    }
}
