using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services;

internal class AdminKafkaService(
    AdminSettings adminSettings,
    IAdminClient adminClient,
    ILogger<AdminKafkaService> logger)
    : IAdminKafkaService
{
    public async Task CreateTopicAsync(string topicName, CancellationToken stoppingToken)
    {
        var topicSpecification = new TopicSpecification
        {
            Name = topicName,
            NumPartitions = adminSettings.NumPartitions,
            ReplicationFactor = adminSettings.ReplicationFactor
        };

        var createTopicOptions = new CreateTopicsOptions
        {
            OperationTimeout = TimeSpan.FromSeconds(adminSettings.OperationTimeoutInSeconds),
            RequestTimeout = TimeSpan.FromSeconds(adminSettings.RequestTimeoutInSeconds)
        };

        var topics = new List<TopicSpecification> { topicSpecification };

        try
        {
            await adminClient.CreateTopicsAsync(topics, createTopicOptions);
            logger.LogInformation($"Topic '{topicName}' created successfully.");
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
}