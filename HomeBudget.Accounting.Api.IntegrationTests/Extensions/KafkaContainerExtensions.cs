using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Confluent.Kafka;
using Testcontainers.Kafka;

using HomeBudget.Accounting.Api.IntegrationTests.Clients;
using HomeBudget.Test.Core.Helpers;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class KafkaContainerExtensions
    {
        public static async Task ResetContainersAsync(this KafkaContainer container)
        {
            var adminConfig = new AdminClientConfig
            {
                BootstrapServers = await container.GetReachableBootstrapAsync()
            };

            using var adminClient = new AdminClientBuilder(adminConfig).Build();

            var metadata = KafkaAdminOperationsClient.GetMetadataSafe(adminClient);
            if (metadata is null)
            {
                return;
            }

            foreach (var topic in KafkaAdminOperationsClient.FilterUserTopics(metadata.Topics))
            {
                await KafkaAdminOperationsClient.TryDeleteTopicRecordsAsync(adminClient, topic);
            }
        }

        public static async Task WaitForKafkaReadyAsync(this KafkaContainer container, TimeSpan timeout)
        {
            var providers = new BootstrapCandidateProvider(container);
            var candidates = providers.BuildCandidates();

            var start = DateTime.UtcNow;
            var log = new Dictionary<string, string>();

            while (!TimeoutHelper.IsTimedOut(start, timeout))
            {
                if (await KafkaAdminOperationsClient.TryConnectToAnyAsync(candidates, log))
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            throw new TimeoutException("Kafka did not become ready in time.");
        }

        public static async Task<string> GetReachableBootstrapAsync(this KafkaContainer container)
        {
            var provider = new BootstrapCandidateProvider(container);
            var candidates = provider.BuildCandidates();

            foreach (var candidate in candidates)
            {
                if (KafkaAdminOperationsClient.CanConnect(candidate))
                {
                    return candidate;
                }

                await Task.Delay(500);
            }

            return container.GetBootstrapAddress();
        }
    }
}
