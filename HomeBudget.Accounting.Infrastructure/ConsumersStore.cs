using System.Collections.Concurrent;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure
{
    internal static class ConsumersStore
    {
        public static readonly ConcurrentDictionary<string, IKafkaConsumer> Consumers = new();
    }
}
