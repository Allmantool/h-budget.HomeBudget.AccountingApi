using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Confluent.Kafka;
using Testcontainers.Kafka;

using HomeBudget.Accounting.Api.IntegrationTests.Clients;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class KafkaContainerExtensions
    {
        private static readonly string[] StaticBootstrapCandidates =
        {
            "localhost:39092",
            "127.0.0.1:39092",
            "localhost:9092",
            "127.0.0.1:9092",
            "localhost:9093",
            "127.0.0.1:9093",
            "localhost:9094",
            "127.0.0.1:9094",
            "localhost:29092",
            "127.0.0.1:29092",
        };

        public static async Task ResetContainersAsync(this KafkaContainer container)
        {
            var adminConfig = new AdminClientConfig
            {
                BootstrapServers = container.GetBootstrapAddress(),
            };

            using var admin = new AdminClientBuilder(adminConfig).Build();

            var meta = admin.GetMetadata(TimeSpan.FromSeconds(10));

            foreach (var topic in meta.Topics)
            {
                // Skip internal Kafka topics
                if (topic.Topic.StartsWith("_"))
                {
                    continue;
                }

                var deleteSpec = topic.Partitions
                    .Select(p => new TopicPartitionOffset(topic.Topic, p.PartitionId, Offset.End))
                    .ToList();

                try
                {
                    await admin.DeleteRecordsAsync(deleteSpec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Kafka purge failed for {topic.Topic}: {ex}");
                }
            }
        }

        public static async Task WaitForKafkaReadyAsync(this KafkaContainer kafkaContainer, TimeSpan timeout)
        {
            var candidates = GetAllBootstrapCandidates(kafkaContainer);
            var start = DateTime.UtcNow;
            var connectionLog = new Dictionary<string, string>();

            while (!TimedOut(start, timeout))
            {
                if (await TryConnectToAnyBootstrapAsync(candidates, connectionLog))
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            throw new TimeoutException("Kafka did not become ready in time.");
        }

        private static IEnumerable<string> GetAllBootstrapCandidates(KafkaContainer container)
        {
            var mapped = new Dictionary<int, int>
            {
                [9092] = container.GetMappedPublicPort(9092),
                [9093] = container.GetMappedPublicPort(9093),
                [9094] = container.GetMappedPublicPort(9094),
                [29092] = container.GetMappedPublicPort(29092)
            };

            var dynamicCandidates = new List<string>();
            var bootstrap = container?.GetBootstrapAddress();

            if (!string.IsNullOrWhiteSpace(bootstrap))
            {
                dynamicCandidates.Add(bootstrap);
                dynamicCandidates.Add(bootstrap.Replace("plaintext://", "", StringComparison.OrdinalIgnoreCase));
            }

            foreach (var portMap in mapped.Keys)
            {
                dynamicCandidates.Add($"{container.IpAddress}:{mapped[portMap]}");
                dynamicCandidates.Add($"{container.Hostname}:{mapped[portMap]}");
            }

            var staticAdjusted = StaticBootstrapCandidates.Select(bootstrapCandidate =>
            {
                var parts = bootstrapCandidate.Split(':');
                if (parts.Length != 2)
                {
                    return bootstrapCandidate;
                }

                var host = parts[0];
                if (!int.TryParse(parts[1], out var port))
                {
                    return bootstrapCandidate;
                }

                return mapped.TryGetValue(port, out var mappedPort)
                    ? $"{host}:{mappedPort}"
                    : bootstrapCandidate;
            });

            return dynamicCandidates
                .Concat(staticAdjusted)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct();
        }

        private static bool TimedOut(DateTime start, TimeSpan timeout) => DateTime.UtcNow - start > timeout;

        private static async Task<bool> TryConnectToAnyBootstrapAsync(
            IEnumerable<string> bootstraps,
            IDictionary<string, string> connectionLog)
        {
            foreach (var bootstrap in bootstraps)
            {
                if (await KafkaAdminClient.TryInitializeKafkaAsync(bootstrap, connectionLog))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
