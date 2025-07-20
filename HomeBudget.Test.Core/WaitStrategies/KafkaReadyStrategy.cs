using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Testcontainers.Kafka;

namespace HomeBudget.Test.Core.WaitStrategies
{
    public static class KafkaReadyStrategy
    {
        public static async Task WaitUntilKafkaReadyAsync(KafkaContainer container, CancellationToken cancellationToken)
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = container.GetBootstrapAddress()
            };

            using var client = new AdminClientBuilder(config).Build();

            var timeout = TimeSpan.FromSeconds(60);
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var metadata = client.GetMetadata(TimeSpan.FromSeconds(1));
                    if (metadata.Brokers.Count > 0)
                    {
                        return;
                    }
                }
                catch
                {
                    // Ignore failures — Kafka may not be up yet
                }

                await Task.Delay(1000, cancellationToken);
            }

            throw new TimeoutException("Kafka failed to become ready within timeout.");
        }
    }
}
