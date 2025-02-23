using System;
using System.Collections.Generic;
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
        public Guid ConsumerId { get; }

        private readonly object _lock = new();
        private const int MaxDegreeOfConcurrency = 10;
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

            ConsumerId = Guid.NewGuid();

            var consumerConfig = new ConsumerConfig
            {
                ClientId = ConsumerId.ToString(),
                BootstrapServers = consumerSettings.BootstrapServers,
                GroupId = $"{consumerSettings.GroupId}-{ConsumerId}",
                AutoOffsetReset = (AutoOffsetReset)consumerSettings.AutoOffsetReset,
                EnableAutoCommit = consumerSettings.EnableAutoCommit,
                AllowAutoCreateTopics = consumerSettings.AllowAutoCreateTopics,
                MaxPollIntervalMs = consumerSettings.MaxPollIntervalMs,
                SessionTimeoutMs = consumerSettings.SessionTimeoutMs,
                HeartbeatIntervalMs = consumerSettings.HeartbeatIntervalMs,
                Debug = "all"
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

            _consumer.Subscribe(topic.ToLower());
            _logger.LogInformation($"Subscribed to topic: {topic.ToLower()}");
        }

        public abstract Task ConsumeAsync(CancellationToken cancellationToken);

        public void Unsubscribe()
        {
            _consumer.Unsubscribe();
        }

        protected virtual async Task ConsumeAsync(
            Func<ConsumeResult<TKey, TValue>, Task> processMessageAsync,
            CancellationToken cancellationToken)
        {
            if (processMessageAsync == null)
            {
                throw new ArgumentNullException(nameof(processMessageAsync));
            }

            using var semaphoreGuard = new SemaphoreGuard(_semaphore);

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

                        await _semaphore.WaitAsync(cancellationToken);

                        var messagePayload = _consumer.Consume();
                        if (messagePayload == null)
                        {
                            continue;
                        }

                        await processMessageAsync(messagePayload);

                        if (!_disposed)
                        {
                            _consumer.Commit(messagePayload);
                        }
                    }
                    catch (ObjectDisposedException ex)
                    {
                        _logger.LogError(ex, "Kafka consumer was accessed after being disposed.");
                        return;
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError($"Consume error: {ex.Error.Reason}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Unexpected error in Kafka consumer: {ex.Message}");
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
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
