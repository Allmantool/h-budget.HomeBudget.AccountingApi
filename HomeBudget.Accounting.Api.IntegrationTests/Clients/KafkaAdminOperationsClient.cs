using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Confluent.Kafka;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    internal static class KafkaAdminOperationsClient
    {
        public static Metadata? GetMetadataSafe(IAdminClient admin)
        {
            try
            {
                return admin.GetMetadata(TimeSpan.FromSeconds(10));
            }
            catch
            {
                return null;
            }
        }

        public static IEnumerable<TopicMetadata> FilterUserTopics(IEnumerable<TopicMetadata> topics)
        {
            return topics.Where(t => !t.Topic.StartsWith("_"));
        }

        public static async Task TryDeleteTopicRecordsAsync(IAdminClient admin, TopicMetadata topic)
        {
            var specs = topic.Partitions
                .Select(p => new TopicPartitionOffset(topic.Topic, p.PartitionId, Offset.End))
                .ToList();

            try
            {
                await admin.DeleteRecordsAsync(specs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kafka purge failed for {topic.Topic}: {ex}");
            }
        }

        public static bool CanConnect(string bootstrap)
        {
            try
            {
                var config = new AdminClientConfig
                {
                    BootstrapServers = bootstrap,
                };

                using var admin = new AdminClientBuilder(config).Build();

                var meta = admin.GetMetadata(TimeSpan.FromSeconds(3));
                return meta.Brokers.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> TryConnectToAnyAsync(
            IEnumerable<string> candidates,
            IDictionary<string, string> log)
        {
            foreach (var candidate in candidates)
            {
                if (await KafkaAdminClient.TryInitializeKafkaAsync(candidate, log))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
