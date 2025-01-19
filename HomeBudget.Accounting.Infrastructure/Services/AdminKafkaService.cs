using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Services
{
    internal class AdminKafkaService(IAdminClient adminClient, ILogger<AdminKafkaService> logger)
        : IAdminKafkaService
    {
        public async Task CreateTopicAsync(string topicName)
        {
            var topicSpecification = new TopicSpecification
            {
                Name = topicName,
                NumPartitions = 3,
                ReplicationFactor = 1
            };

            var createTopicOptions = new CreateTopicsOptions
            {
                OperationTimeout = TimeSpan.FromSeconds(30),
                RequestTimeout = TimeSpan.FromSeconds(30)
            };

            var topics = new List<TopicSpecification> { topicSpecification };

            try
            {
                await adminClient.CreateTopicsAsync(
                    topics,
                    createTopicOptions);
            }
            catch (CreateTopicsException ex)
            {
                foreach (var result in ex.Results)
                {
                    logger.LogError($"An error occurred creating topic {result.Topic}: {result.Error.Reason}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"An unexpected error occurred: {ex.Message}");
            }
        }

        public void Dispose()
        {
            adminClient.Dispose();
        }
    }
}
