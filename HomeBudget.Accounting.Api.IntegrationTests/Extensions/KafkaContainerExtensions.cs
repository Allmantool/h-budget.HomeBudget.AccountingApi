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
            "127.0.0.1:9092",
            "localhost:9092",
            "127.0.0.1:29092",
            "localhost:29092",
            "test-kafka:9092",
            "127.0.0.1:9093",
            "localhost:9093",
            "127.0.0.1:9094",
            "localhost:9094",
            "test-kafka:9094",
            "172.18.0.3:9093",
            "172.18.0.3:9092"
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
            var dynamicCandidates = new[]
            {
                container?.GetBootstrapAddress(),
                container?.GetBootstrapAddress()?.Replace("plaintext://", "", StringComparison.OrdinalIgnoreCase)
            };

            return StaticBootstrapCandidates
                .Concat(dynamicCandidates)
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
            try
            {
                using var admin = CreateAdminClient(bootstrap);
                var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));

                if (metadata.Brokers.IsNullOrEmpty())
                {
                    return false;
                }

                await EnsureRequiredTopicsExistAsync(admin, metadata);
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

        private static async Task EnsureRequiredTopicsExistAsync(IAdminClient admin, Metadata metadata)
        {
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
                await admin.CreateTopicsAsync(missingTopics);
            }
        }
    }
}
