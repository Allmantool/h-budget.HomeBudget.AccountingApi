using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services;

internal sealed class KafkaTopicManager(
    AdminSettings settings,
    IAdminClient adminClient,
    IConsumer<Ignore, Ignore> offsetConsumer,
    ILogger<KafkaTopicManager> logger)
    : ITopicManager
{
    public async Task CreateAsync(string topicName, CancellationToken token)
    {
        var topicSpec = new TopicSpecification
        {
            Name = topicName.ToLower(),
            NumPartitions = settings.NumPartitions,
            ReplicationFactor = settings.ReplicationFactor
        };

        var options = new CreateTopicsOptions
        {
            OperationTimeout = TimeSpan.FromSeconds(settings.OperationTimeoutInSeconds),
            RequestTimeout = TimeSpan.FromSeconds(settings.RequestTimeoutInSeconds)
        };

        try
        {
            await adminClient.CreateTopicsAsync([topicSpec], options);
            logger.LogInformation("Topic '{Topic}' created.", topicName);
        }
        catch (CreateTopicsException ex)
        {
            foreach (var result in ex.Results)
            {
                logger.LogError(ex, "Failed to create topic '{Topic}': {Reason}", result.Topic, result.Error.Reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create topic");
        }
    }

    public async Task DeleteAsync(string topic)
    {
        try
        {
            await adminClient.DeleteTopicsAsync([topic]);
            logger.LogInformation("Topic '{Topic}' deleted.", topic);
        }
        catch (DeleteTopicsException ex)
        {
            logger.LogError(ex, "Failed to delete topic '{Topic}': {Reason}", topic, ex.Results.FirstOrDefault()?.Error.Reason);
        }
    }

    public IReadOnlyCollection<string> GetAll()
    {
        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(settings.OperationTimeoutInSeconds));
        return metadata.Topics?.Select(t => t.Topic).ToList() ?? [];
    }

    public long GetTopicLag(string topic)
    {
        if (topic.StartsWith("__"))
        {
            return 0;
        }

        var metadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(settings.RequestTimeoutInSeconds));
        var topicMetadata = metadata.Topics.FirstOrDefault(t => string.Equals(t.Topic, topic, StringComparison.OrdinalIgnoreCase));

        if (topicMetadata == null || topicMetadata.Partitions.IsNullOrEmpty())
        {
            return 0;
        }

        var partitions = topicMetadata.Partitions
            .Select(p => new TopicPartition(topic, new Partition(p.PartitionId)))
            .ToList();

        var committed = offsetConsumer.Committed(partitions, TimeSpan.FromSeconds(settings.RequestTimeoutInSeconds));
        long totalLag = 0;

        foreach (var partition in partitions)
        {
            var committedOffset = committed.FirstOrDefault(c => c.TopicPartition == partition);

            var watermark = offsetConsumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(settings.RequestTimeoutInSeconds));

            if (committedOffset != null && committedOffset.Offset != Offset.Unset)
            {
                totalLag += watermark.High.Value - committedOffset.Offset.Value;
            }
            else
            {
                totalLag += watermark.High.Value - watermark.Low.Value;
            }
        }

        return totalLag;
    }

    public async Task<bool> HasActiveConsumerAsync(string topic, string consumerGroupId)
    {
        var groups = await adminClient.ListConsumerGroupsAsync();
        if (!groups.Valid.Any(g => g.GroupId == consumerGroupId))
        {
            return false;
        }

        var groupInfo = await adminClient.DescribeConsumerGroupsAsync([consumerGroupId]);

        return groupInfo.ConsumerGroupDescriptions
            .SelectMany(desc => desc.Members)
            .SelectMany(m => m.Assignment?.TopicPartitions ?? [])
            .Any(tp => tp.Topic == topic);
    }
}
