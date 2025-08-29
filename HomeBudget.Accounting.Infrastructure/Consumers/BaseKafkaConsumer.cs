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
                // GroupInstanceId = ConsumerId,
                ClientId = _consumerSettings.ClientId,
                BootstrapServers = _consumerSettings.BootstrapServers,
                GroupId = $"{_consumerSettings.GroupId}",
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
                    BaseKafkaConsumerLogs.SubscriptionsAccessedOnDisposed(_logger);
                    return Array.Empty<string>();
                }

                try
                {
                    return _consumer.Subscription.AsReadOnly();
                }
                catch (ObjectDisposedException ex)
                {
                    BaseKafkaConsumerLogs.SubscriptionsAccessedOnDisposedError(_logger, ex);
                    return Array.Empty<string>();
                }
            }
        }

        public abstract Task ConsumeAsync(CancellationToken stoppingToken);

        public bool IsAlive
        {
            get
            {
                if (_disposed || _consumer == null)
                {
                    return false;
                }

                try
                {
                    var _ = _consumer.Subscription;
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (KafkaException ex) when (ex.Error.IsFatal)
                {
                    BaseKafkaConsumerLogs.FatalKafkaError(_logger, ex);
                    return false;
                }
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
                    if (_disposed || _consumer == null)
                    {
                        BaseKafkaConsumerLogs.ConsumerDisposed(_logger);
                        break;
                    }

                    try
                    {
                        var consumeResult = _consumer.Consume(TimeSpan.FromMilliseconds(_consumerSettings.ConsumeDelayInMilliseconds));

                        if (consumeResult == null)
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
                            BaseKafkaConsumerLogs.TopicPartitionNotFound(_logger, ex.Error.Reason);

                            await Task.Delay(_consumerSettings.HeartbeatIntervalMs, cancellationToken);
                        }
                        else
                        {
                            BaseKafkaConsumerLogs.ConsumeError(_logger, ex.Error.Reason, ex);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        BaseKafkaConsumerLogs.ConsumerLoopCanceled(_logger);
                        break;
                    }
                    catch (Exception ex)
                    {
                        BaseKafkaConsumerLogs.UnhandledErrorInLoop(_logger, ex.Message, ex);
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
                BaseKafkaConsumerLogs.CloseAlreadyDisposedConsumer(_logger);
                return;
            }

            try
            {
                _consumer.Close();
                _consumer.Dispose();
            }
            catch (Exception ex)
            {
                BaseKafkaConsumerLogs.ErrorWhileClosingConsumer(_logger, ex.Message, ex);
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
                BaseKafkaConsumerLogs.SubscribeOnDisposed(_logger, topic);
                return;
            }

            var topicName = topic.ToLower();

            _consumer.Subscribe(topicName);
            _subscribedTopics.Add(topicName);

            BaseKafkaConsumerLogs.SubscribedToTopic(_logger, topicName, ConsumerId, string.Join(',', _subscribedTopics));
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
                BaseKafkaConsumerLogs.ErrorWhileClosingConsumer(_logger, ex.Message, ex);
            }

            BaseKafkaConsumerLogs.UnsubscribedConsumer(_logger, ConsumerId, string.Join(",", _subscribedTopics));
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
                            BaseKafkaConsumerLogs.CloseSkippedDisposed(_logger);
                        }
                        catch (Exception ex)
                        {
                            BaseKafkaConsumerLogs.ErrorWhileClosingConsumer(_logger, ex.Message, ex);
                        }

                        try
                        {
                            _consumer.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            BaseKafkaConsumerLogs.DisposeSkippedDisposed(_logger);
                        }
                        catch (Exception ex)
                        {
                            BaseKafkaConsumerLogs.ErrorWhileDisposingConsumer(_logger, ex);
                        }
                    }
                }

                _disposed = true;

                BaseKafkaConsumerLogs.DisposedConsumer(_logger, ConsumerId, string.Join(",", _subscribedTopics ?? Enumerable.Empty<string>()));
            }
        }
    }
}
