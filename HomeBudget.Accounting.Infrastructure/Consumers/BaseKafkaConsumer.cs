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

            var consumerId = Guid.NewGuid().ToString();

            var consumerConfig = new ConsumerConfig
            {
                ClientId = consumerId,
                BootstrapServers = consumerSettings.BootstrapServers,
                GroupId = $"{consumerSettings.GroupId}-{consumerId}",
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

        public IReadOnlyCollection<string> Subscriptions => _consumer.Subscription.AsReadOnly();

        public void Subscribe(string topic)
        {
            if (string.IsNullOrEmpty(topic))
            {
                throw new ArgumentNullException(nameof(topic));
            }

            _consumer.Subscribe(topic.ToLower());
            _logger.LogInformation($"Subscribed to topic: {topic.ToLower()}");
        }

        public abstract Task ConsumeAsync(CancellationToken cancellationToken);

        protected virtual async Task ConsumeAsync(
            Func<ConsumeResult<TKey, TValue>, Task> processMessageAsync,
            CancellationToken cancellationToken)
        {
            if (processMessageAsync == null)
            {
                throw new ArgumentNullException(nameof(processMessageAsync));
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (_consumer.Subscription.IsNullOrEmpty())
                        {
                            continue;
                        }

                        var messagePayload = _consumer.Consume(TimeSpan.FromMilliseconds(500));

                        if (messagePayload == null)
                        {
                            continue;
                        }

                        await processMessageAsync(messagePayload);

                        await Task.Run(() => _consumer.Commit(messagePayload), cancellationToken);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError($"Consume error: {ex.Error.Reason}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Consume error: {ex.Message}");
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
            try
            {
                _consumer.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error while closing consumer: {ex.Message}");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _consumer?.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Unsubscribe()
        {
            _consumer.Unsubscribe();
        }
    }
}
