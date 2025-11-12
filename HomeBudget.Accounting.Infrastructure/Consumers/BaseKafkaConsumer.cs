using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Logs;

using HomeBudget.Core.Models;
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
        private readonly ConsumerSettings _consumerSettings;
        private bool _disposed;

        protected BaseKafkaConsumer(
            KafkaOptions kafkaOptions,
            ILogger<BaseKafkaConsumer<TKey, TValue>> logger)
        {
            if (kafkaOptions?.ConsumerSettings == null)
            {
                throw new ArgumentNullException(nameof(KafkaOptions.ConsumerSettings));
            }

            _consumerSettings = kafkaOptions.ConsumerSettings;

            ConsumerId = _consumerSettings.ClientId;

            var consumerConfig = new ConsumerConfig
            {
                GroupInstanceId = $"{_consumerSettings.GroupId}-{_consumerSettings.ClientId}",
                GroupId = $"{_consumerSettings.GroupId}",
                ClientId = _consumerSettings.ClientId,
                BootstrapServers = _consumerSettings.BootstrapServers,
                AutoOffsetReset = (AutoOffsetReset)_consumerSettings.AutoOffsetReset,
                EnableAutoCommit = _consumerSettings.EnableAutoCommit,
                AllowAutoCreateTopics = _consumerSettings.AllowAutoCreateTopics,
                MaxPollIntervalMs = _consumerSettings.MaxPollIntervalMs,
                SessionTimeoutMs = _consumerSettings.SessionTimeoutMs,
                HeartbeatIntervalMs = _consumerSettings.HeartbeatIntervalMs,
                Debug = _consumerSettings.Debug,
                FetchMaxBytes = _consumerSettings.FetchMaxBytes,
                FetchWaitMaxMs = _consumerSettings.FetchWaitMaxMs,
                PartitionAssignmentStrategy = (PartitionAssignmentStrategy)_consumerSettings.PartitionAssignmentStrategy
            };

            _logger = logger;

            _consumer = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                .SetErrorHandler((ctx, error) => BaseKafkaConsumerLogs.KafkaError(_logger, error.Reason, ctx?.Name, ctx?.MemberId))
                .SetLogHandler((_, logMessage) => BaseKafkaConsumerLogs.KafkaLog(_logger, logMessage.Message))
                .SetPartitionsRevokedHandler((c, partitions) => BaseKafkaConsumerLogs.PartitionsRevoked(_logger, string.Join(", ", partitions)))
                .SetPartitionsAssignedHandler((c, partitions) => BaseKafkaConsumerLogs.PartitionsAssigned(_logger, string.Join(", ", partitions)))
                .Build();
        }

        public IReadOnlyCollection<string> Subscriptions
        {
            get
            {
                if (_disposed || _consumer == null)
                {
                    _logger.SubscriptionsAccessedOnDisposed();
                    return Array.Empty<string>();
                }

                try
                {
                    return _consumer.Subscription.AsReadOnly();
                }
                catch (ObjectDisposedException ex)
                {
                    _logger.SubscriptionsAccessedOnDisposedError(ex);
                    return Array.Empty<string>();
                }
            }
        }

        public abstract Task ConsumeAsync(CancellationToken stoppingToken);

        public bool IsAlive()
        {
            if (_disposed || _consumer is null)
            {
                return false;
            }

            try
            {
                var assigned = _consumer.Assignment;
                var subscribed = _consumer.Subscription;

                return assigned.Count != 0 || subscribed.Count != 0;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (KafkaException ex)
            {
                if (ex.Error.IsFatal)
                {
                    _logger.FatalKafkaError(ex);
                }
                else
                {
                    _logger.TransientKafkaError(ex);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error checking consumer liveness");
                return false;
            }
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
                    if (_disposed || _consumer is null)
                    {
                        BaseKafkaConsumerLogs.ConsumerDisposed(_logger);
                        break;
                    }

                    try
                    {
                        var consumeResult = _consumer.Consume(TimeSpan.FromMilliseconds(_consumerSettings.ConsumeDelayInMilliseconds));

                        if (consumeResult is null)
                        {
                            await Task.Delay((int)_consumerSettings.ConsumeDelayInMilliseconds, cancellationToken);
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
                            _logger.TopicPartitionNotFound(ex.Error.Reason);

                            await Task.Delay(_consumerSettings.HeartbeatIntervalMs, cancellationToken);
                        }
                        else
                        {
                            _logger.ConsumeError(ex.Error.Reason, ex);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.ConsumerLoopCanceled();
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.UnhandledErrorInLoop(ex.Message, ex);
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
                _logger.CloseAlreadyDisposedConsumer();
                return;
            }

            try
            {
                _consumer.Close();
                _consumer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.ErrorWhileClosingConsumer(ex.Message, ex);
            }
            finally
            {
                _disposed = true;
            }
        }

        public void Subscribe(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentNullException(nameof(topic));
            }

            if (_disposed || _consumer == null)
            {
                _logger.SubscribeOnDisposed(topic);
                return;
            }

            var topicName = topic.ToLower(CultureInfo.CurrentCulture);

            _consumer.Subscribe(topicName);
            _subscribedTopics.Add(topicName);

            _logger.SubscribedToTopic(topicName, ConsumerId, string.Join(',', _subscribedTopics));
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
                _logger.ErrorWhileClosingConsumer(ex.Message, ex);
            }

            _logger.UnsubscribedConsumer(ConsumerId, string.Join(",", _subscribedTopics));
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
                            _logger.CloseSkippedDisposed();
                        }
                        catch (Exception ex)
                        {
                            _logger.ErrorWhileClosingConsumer(ex.Message, ex);
                        }

                        try
                        {
                            _consumer.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.DisposeSkippedDisposed();
                        }
                        catch (Exception ex)
                        {
                            _logger.ErrorWhileDisposingConsumer(ex);
                        }
                    }
                }

                _disposed = true;

                _logger.DisposedConsumer(ConsumerId, string.Join(",", _subscribedTopics ?? Enumerable.Empty<string>()));
            }
        }
    }
}
