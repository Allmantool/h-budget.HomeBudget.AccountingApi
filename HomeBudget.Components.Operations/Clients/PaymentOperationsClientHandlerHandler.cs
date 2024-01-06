using System;

using Confluent.Kafka;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Components.Operations.Clients
{
    internal sealed class PaymentOperationsClientHandlerHandler : IKafkaClientHandler
    {
        private IProducer<byte[], byte[]> KafkaProducer { get; }

        public PaymentOperationsClientHandlerHandler(IOptions<KafkaOptions> options)
        {
            var kafkaOptions = options.Value;
            var producerSettings = kafkaOptions.ProducerSettings;

            var conf = new ProducerConfig(new ProducerConfig
            {
                ClientId = "PaymentOperationsClientId",
                BootstrapServers = producerSettings.BootstrapServers,
                MessageTimeoutMs = producerSettings.MessageTimeoutMs ?? TimeSpan.FromSeconds(3).Microseconds,
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
