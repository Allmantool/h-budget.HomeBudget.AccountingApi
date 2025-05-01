using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Consumers
{
    public abstract class BaseKafkaConsumer<TKey, TValue> : IKafkaConsumer
    {
        public string ConsumerId { get; }

        private static readonly ActivitySource ActivitySource = new("HomeBudget.KafkaConsumer");
        private readonly Lock _lock = new();
        private ConcurrentBag<string> _subscribedTopics = new ConcurrentBag<string>();
        private const int MaxDegreeOfConcurrency = 150;
        private readonly SemaphoreSlim _semaphore = new(MaxDegreeOfConcurrency);

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
                ClientId = consumerSettings.ClientId,
                BootstrapServers = consumerSettings.BootstrapServers,
                GroupId = $"{consumerSettings.GroupId}",
                GroupInstanceId = ConsumerId,
                AutoOffsetReset = (AutoOffsetReset)consumerSettings.AutoOffsetReset,
                EnableAutoCommit = consumerSettings.EnableAutoCommit,
                AllowAutoCreateTopics = consumerSettings.AllowAutoCreateTopics,
                MaxPollIntervalMs = consumerSettings.MaxPollIntervalMs,
                SessionTimeoutMs = consumerSettings.SessionTimeoutMs,
                HeartbeatIntervalMs = consumerSettings.HeartbeatIntervalMs,
                Debug = consumerSettings.Debug,
                FetchMaxBytes = consumerSettings.FetchMaxBytes,
                PartitionAssignmentStrategy = (PartitionAssignmentStrategy)consumerSettings.PartitionAssignmentStrategy
            };

            _logger = logger;

            _consumer = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                .SetErrorHandler((_, error) => _logger.LogError($"Error: {error.Reason}"))
                .SetLogHandler((_, logMessage) => _logger.LogInformation($"Log: {logMessage.Message}"))
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

        public void Assign(string topic)
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

            var topicPartition = new TopicPartition(topicName, 0);
            _consumer.Assign(topicPartition);
            _subscribedTopics.Add(topicName);

            _logger.LogInformation($"Subscribed to topic: {topicName}, consumer {ConsumerId} topics: {string.Join(',', _subscribedTopics)} ");
        }

        public abstract Task ConsumeAsync(CancellationToken stoppingToken);

        public void Unassign()
        {
            _consumer.Unassign();
            _logger.LogInformation($"The consumer {ConsumerId} has been unsubscribed. Related topics {string.Join(",", _subscribedTopics)}");
        }

        protected virtual async Task ConsumeAsync(
            Func<ConsumeResult<TKey, TValue>, Task> processMessageAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(processMessageAsync);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (_disposed || _consumer == null)
                        {
                            _logger.LogWarning("Kafka consumer was disposed during message consumption.");
                            return;
                        }

                        if (_consumer.Subscription.IsNullOrEmpty())
                        {
                            continue;
                        }

                        _logger.LogDebug("Semaphore current count: {Count}", _semaphore.CurrentCount);

                        await _semaphore.WaitAsync(cancellationToken);

                        _ = Task.Run(
                            async () =>
                            {
                                using var semaphoreGuard = new SemaphoreGuard(_semaphore);

                                using var activity = ActivitySource.StartActivity("KafkaMessage.Consume");

                                try
                                {
                                    var messagePayload = _consumer.Consume(cancellationToken);

                                    activity?.SetTag("messaging.system", "kafka");
                                    activity?.SetTag("messaging.destination", messagePayload?.Topic);
                                    activity?.SetTag("messaging.kafka.partition", messagePayload?.Partition.Value);
                                    activity?.SetTag("messaging.kafka.offset", messagePayload?.Offset.Value);
                                    activity?.SetTag("messaging.message_id", messagePayload?.Message?.Key?.ToString());

                                    if (messagePayload != null)
                                    {
                                        await processMessageAsync(messagePayload);
                                        if (!_disposed)
                                        {
                                            _consumer.Commit(messagePayload);
                                        }
                                    }

                                    activity?.SetStatus(ActivityStatusCode.Ok);
                                }
                                catch (Exception ex)
                                {
                                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                                    _logger.LogError(ex, $"Unexpected error in Kafka consumer: {ex.Message}");
                                }
                            }, cancellationToken);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        _logger.LogError(ex, "Kafka consumer was accessed after being disposed.");
                        return;
                    }
                    catch (ConsumeException ex)
                    {
                        if (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                        {
                            _logger.LogWarning($"Topic/partition not found: {ex.Error.Reason}. Retrying...");
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                        }
                        else
                        {
                            _logger.LogError(ex, $"Consume error: {ex.Error.Reason}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Unexpected error in Kafka consumer: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation("Consumer loop canceled. '{Exception}' error: {ExceptionDetails}", nameof(OperationCanceledException), ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Consumer loop canceled. Error: {ExceptionDetails}", ex.Message);
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
                    _semaphore?.Dispose();
                    _consumer?.Dispose();
                }

                _disposed = true;

                _logger.LogInformation($"The consumer {ConsumerId} has been disposed. Related topics {string.Join(",", _subscribedTopics)}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
            _consumer.Unsubscribe();
            _logger.LogInformation($"The consumer {ConsumerId} has been unsubscribed. Related topics {string.Join(",", _subscribedTopics)}");
        }
    }
}
