using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Consumers
{
    public abstract class BaseKafkaConsumer<TKey, TValue> : IKafkaConsumer
    {
        public string ConsumerId { get; }

        private static readonly ActivitySource ActivitySource = new("HomeBudget.KafkaConsumer");
        private static readonly ConcurrentBag<string> _subscribedTopics = new ConcurrentBag<string>();

        private readonly Lock _lock = new();
        private readonly ILogger<BaseKafkaConsumer<TKey, TValue>> _logger;
        private readonly IConsumer<TKey, TValue> _consumer;
        private bool _disposed;

        protected BaseKafkaConsumer(
            KafkaOptions kafkaOptions,
            ILogger<BaseKafkaConsumer<TKey, TValue>> logger)
        {
            if (kafkaOptions?.ConsumerSettings == null)
            {
                throw new ArgumentNullException(nameof(KafkaOptions.ConsumerSettings));
            }

            var consumerSettings = kafkaOptions.ConsumerSettings;

            ConsumerId = consumerSettings.ClientId;

            var consumerConfig = new ConsumerConfig
            {
                // GroupInstanceId = ConsumerId,
                ClientId = consumerSettings.ClientId,
                BootstrapServers = consumerSettings.BootstrapServers,
                GroupId = $"{consumerSettings.GroupId}",
                AutoOffsetReset = (AutoOffsetReset)consumerSettings.AutoOffsetReset,
                EnableAutoCommit = consumerSettings.EnableAutoCommit,
                AllowAutoCreateTopics = consumerSettings.AllowAutoCreateTopics,
                MaxPollIntervalMs = consumerSettings.MaxPollIntervalMs,
                SessionTimeoutMs = consumerSettings.SessionTimeoutMs,
                HeartbeatIntervalMs = consumerSettings.HeartbeatIntervalMs,
                Debug = consumerSettings.Debug,
                FetchMaxBytes = consumerSettings.FetchMaxBytes,
                FetchWaitMaxMs = consumerSettings.FetchWaitMaxMs,
                PartitionAssignmentStrategy = (PartitionAssignmentStrategy)consumerSettings.PartitionAssignmentStrategy
            };

            _logger = logger;

            _consumer = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                .SetErrorHandler((ctx, error) =>
                {
                    _logger.LogError($"Error: {error.Reason} {ctx?.Name} {ctx?.MemberId}");
                })
                .SetLogHandler((_, logMessage) =>
                {
                    _logger.LogInformation($"Log: {logMessage.Message}");
                })
                .SetPartitionsRevokedHandler((c, partitions) =>
                {
                    _logger.LogInformation($"Log: {$"Partitions revoked: [{string.Join(", ", partitions)}]"}");
                })
                .SetPartitionsAssignedHandler((c, partitions) =>
                {
                    _logger.LogInformation($"Partitions assigned: [{string.Join(", ", partitions)}]");
                })
                .Build();
        }

        public IReadOnlyCollection<string> Subscriptions
        {
            get
            {
                if (_disposed || _consumer == null)
                {
                    _logger.LogWarning("Attempted to access Subscriptions on a disposed consumer.");
                    return Array.Empty<string>();
                }

                try
                {
                    return _consumer.Subscription.AsReadOnly();
                }
                catch (ObjectDisposedException ex)
                {
                    _logger.LogError(ex, "Attempted to access Subscriptions on a disposed Kafka consumer.");
                    return Array.Empty<string>();
                }
            }
        }

        public abstract Task ConsumeAsync(CancellationToken stoppingToken);

        protected virtual async Task ConsumeAsync(
            Func<ConsumeResult<TKey, TValue>, Task> processMessageAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(processMessageAsync);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_disposed || _consumer == null)
                    {
                        _logger.LogWarning("Kafka consumer is disposed.");
                        break;
                    }

                    try
                    {
                        var consumeResult = _consumer.Consume(TimeSpan.FromMilliseconds(500));

                        if (consumeResult == null)
                        {
                            await Task.Delay(300, cancellationToken); // prevent busy loop
                            continue;
                        }

                        using var activity = ActivitySource.StartActivity("KafkaMessage.Consume");

                        activity?.SetTag("messaging.system", "kafka");
                        activity?.SetTag("messaging.destination", consumeResult.Topic);
                        activity?.SetTag("messaging.kafka.partition", consumeResult.Partition.Value);
                        activity?.SetTag("messaging.kafka.offset", consumeResult.Offset.Value);
                        activity?.SetTag("messaging.message_id", consumeResult.Message?.Key?.ToString());

                        await processMessageAsync(consumeResult);

                        if (!_disposed)
                        {
                            _consumer.Commit(consumeResult);
                        }

                        activity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    catch (ConsumeException ex)
                    {
                        if (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                        {
                            _logger.LogWarning("Topic/partition not found: {Reason}. Retrying...", ex.Error.Reason);
                            await Task.Delay(5000, cancellationToken);
                        }
                        else
                        {
                            _logger.LogError(ex, "Consume error: {Reason}", ex.Error.Reason);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Consumer loop canceled.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled error in Kafka consumer loop: {Message}", ex.Message);
                    }
                }
            }
            finally
            {
                CloseConsumer();
            }
        }

        private void CloseConsumer()
        {
            if (_disposed || _consumer == null)
            {
                _logger.LogWarning("Attempted to close an already disposed consumer.");
                return;
            }

            try
            {
                _consumer.Close();
                _consumer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error while closing consumer: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }

        public void Subscribe(string topic)
        {
            if (string.IsNullOrEmpty(topic))
            {
                throw new ArgumentNullException(nameof(topic));
            }

            if (_disposed || _consumer == null)
            {
                _logger.LogError("Attempted to subscribe to topic '{Topic}' on a disposed Kafka consumer.", topic);
                return;
            }

            var topicName = topic.ToLower();

            _consumer.Subscribe(topicName);
            _subscribedTopics.Add(topicName);

            _logger.LogInformation($"Subscribed to topic: {topicName}, consumer {ConsumerId} topics: {string.Join(',', _subscribedTopics)} ");
        }

        public void UnSubscribe()
        {
            try
            {
                // _consumer?.Unsubscribe();
                CloseConsumer();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing Kafka consumer.");
            }

            _logger.LogInformation($"The consumer {ConsumerId} has been unsubscribed. Related topics {string.Join(",", _subscribedTopics)}");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                if (disposing)
                {
                    if (_consumer != null)
                    {
                        try
                        {
                            _consumer.Close();
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogWarning("Kafka consumer already disposed. Skipping Close().");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error while closing Kafka consumer.");
                        }

                        try
                        {
                            _consumer.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogWarning("Kafka consumer already disposed. Skipping Dispose().");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error while disposing Kafka consumer.");
                        }
                    }
                }

                _disposed = true;

                _logger.LogInformation($"The consumer {ConsumerId} has been disposed. Related topics: {string.Join(",", _subscribedTopics ?? Enumerable.Empty<string>())}");
            }
        }
    }
}
