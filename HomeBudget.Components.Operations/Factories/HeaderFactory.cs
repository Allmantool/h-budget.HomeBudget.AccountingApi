using System.Text;

using Confluent.Kafka;

namespace HomeBudget.Components.Operations.Factories
{
    internal static class HeaderFactory
    {
        public static Header String(string key, string value)
            => new(key, Encoding.UTF8.GetBytes(value));
    }
}
