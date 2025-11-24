using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;

using HomeBudget.Core.Constants;
using HomeBudget.Core.Exceptions;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class KafkaContainerExtensions
    {
        private static readonly string[] StaticBootstrapCandidates =
        {
            "localhost:9092",
            "127.0.0.1:9092",
            "localhost:9093",
            "127.0.0.1:9093",
            "localhost:9094",
            "127.0.0.1:9094",
            "localhost:29092",
            "127.0.0.1:29092",
        };

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

            var staticAdjusted = StaticBootstrapCandidates.Select(original =>
            {
                var parts = original.Split(':');
                if (parts.Length != 2)
                {
                    return original;
                }

                var host = parts[0];
                if (!int.TryParse(parts[1], out var port))
                {
                    return original;
                }

                return mapped.TryGetValue(port, out var mappedPort)
                    ? $"{host}:{mappedPort}"
                    : original;
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
                if (await TryInitializeKafkaAsync(bootstrap, connectionLog))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<bool> TryInitializeKafkaAsync(
            string bootstrap,
            IDictionary<string, string> connectionLog)
        {
            var clientDefaultTimeoutInSeconds = TimeSpan.FromSeconds(15);

            try
            {
                using var adminClient = CreateAdminClient(bootstrap);
                var metadata = adminClient.GetMetadata(clientDefaultTimeoutInSeconds);

                if (metadata.Brokers.IsNullOrEmpty())
                {
                    return false;
                }

                var existingTopics = metadata.Topics.Select(t => t.Topic).ToHashSet();

                var missingTopics = new List<TopicSpecification>();

                if (!existingTopics.Contains(BaseTopics.AccountingAccounts))
                {
                    missingTopics.Add(new TopicSpecification
                    {
                        Name = BaseTopics.AccountingAccounts,
                        NumPartitions = 1,
                        ReplicationFactor = 1
                    });
                }

                if (!existingTopics.Contains(BaseTopics.AccountingPayments))
                {
                    missingTopics.Add(new TopicSpecification
                    {
                        Name = BaseTopics.AccountingPayments,
                        NumPartitions = 5,
                        ReplicationFactor = 1
                    });
                }

                if (missingTopics.Count > 0)
                {
                    await adminClient.CreateTopicsAsync(missingTopics, new CreateTopicsOptions
                    {
                        OperationTimeout = clientDefaultTimeoutInSeconds,
                        RequestTimeout = clientDefaultTimeoutInSeconds
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                connectionLog.TryAdd(bootstrap, ex.Message);
                return false;
            }
        }

        private static IAdminClient CreateAdminClient(string bootstrap) =>
            new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = bootstrap,
                SocketTimeoutMs = 50_000,
                ConnectionsMaxIdleMs = 10_000,
                MessageMaxBytes = 1_000_000_000
            }).Build();
    }
}
