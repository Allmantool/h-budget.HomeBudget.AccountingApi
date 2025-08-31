using System.Collections.Concurrent;
using System.Collections.Generic;

using HomeBudget.Accounting.Infrastructure.Consumers;

namespace HomeBudget.Accounting.Infrastructure.Helpers
{
    internal static class ConsumersStore
    {
        public static readonly ConcurrentDictionary<string, IEnumerable<BaseKafkaConsumer<string, string>>> Consumers = new();
    }
}
