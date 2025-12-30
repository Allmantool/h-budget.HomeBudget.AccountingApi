using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

using HomeBudget.Core.Constants;
using HomeBudget.Core.Exstensions;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    internal static class KafkaAdminClient
    {
        public static async Task<bool> TryInitializeKafkaAsync(
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
