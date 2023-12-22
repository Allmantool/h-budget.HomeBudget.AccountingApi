using System;

using Confluent.Kafka;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public interface IKafkaClientHandler : IDisposable
    {
        public Handle Handle { get; }
    }
}
