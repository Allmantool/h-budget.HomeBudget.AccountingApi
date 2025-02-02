using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace HomeBudget.Accounting.Infrastructure.Services
{
    internal class AdminKafkaService(IAdminClient adminClient, ILogger<AdminKafkaService> logger)
        : IAdminKafkaService
    {
        public async Task CreateTopicAsync(string topicName, CancellationToken stoppingToken)
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
