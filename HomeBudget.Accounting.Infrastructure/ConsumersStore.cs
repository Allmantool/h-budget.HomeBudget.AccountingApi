using System.Collections.Concurrent;
using System.Collections.Generic;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;

namespace HomeBudget.Accounting.Infrastructure
{
    internal static class ConsumersStore
    {
        public static readonly ConcurrentDictionary<string, IEnumerable<IKafkaConsumer>> Consumers = new();
    }
}
