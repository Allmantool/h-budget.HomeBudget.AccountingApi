using System.Text;

using Confluent.Kafka;

using HomeBudget.Core.Exstensions;

namespace HomeBudget.Accounting.Infrastructure.Extensions
{
    public static class KafkaMessageHeadersExtensions
    {
        public static void TryAddHeader(
            this Headers headers,
            string key,
            string value)
        {
            if (headers.IsNullOrEmpty())
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                headers.Add(key, Encoding.UTF8.GetBytes(value));
            }
        }
    }
}
