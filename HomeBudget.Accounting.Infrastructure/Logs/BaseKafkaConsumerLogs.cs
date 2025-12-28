using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Logs
{
    internal static partial class BaseKafkaConsumerLogs
    {
        [LoggerMessage(
            EventId = 1026,
            Level = LogLevel.Warning,
            Message = "Error while disposing Kafka consumer.")]
        public static partial void ErrorWhileDisposingConsumer(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 1024,
            Level = LogLevel.Error,
            Message = "Attempted to access Subscriptions on a disposed Kafka consumer.")]
        public static partial void SubscriptionsAccessedOnDisposedError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 1023,
            Level = LogLevel.Information,
            Message = "Partitions assigned: [{Partitions}]")]
        public static partial void PartitionsAssigned(
            this ILogger logger,
            string partitions);

        [LoggerMessage(
            EventId = 1022,
            Level = LogLevel.Information,
            Message = "Partitions revoked: [{Partitions}]")]
        public static partial void PartitionsRevoked(
            this ILogger logger,
            string partitions);

        [LoggerMessage(
            EventId = 1021,
            Level = LogLevel.Information,
            Message = "Kafka log: {Message}")]
        public static partial void KafkaLog(
            this ILogger logger,
            string message);

        [LoggerMessage(
            EventId = 1020,
            Level = LogLevel.Error,
            Message = "Kafka error: {Reason}. Context: Name={ContextName}, MemberId={MemberId}")]
        public static partial void KafkaError(
            this ILogger logger,
            string reason,
            string contextName,
            string memberId);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Warning,
            Message = "Attempted to access Subscriptions on a disposed consumer.")]
        public static partial void SubscriptionsAccessedOnDisposed(this ILogger logger);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Error,
            Message = "Fatal Kafka error, consumer not alive.")]
        public static partial void FatalKafkaError(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Error,
            Message = "Transient Kafka error, consumer not alive.")]
        public static partial void TransientKafkaError(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Warning,
            Message = "Kafka consumer is disposed.")]
        public static partial void ConsumerDisposed(this ILogger logger);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Warning,
            Message = "Topic/partition not found: {Reason}. Retrying...")]
        public static partial void TopicPartitionNotFound(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Error,
            Message = "Consume error: {Reason}")]
        public static partial void ConsumeError(this ILogger logger, string reason, Exception exception);

        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Information,
            Message = "Consumer loop canceled.")]
        public static partial void ConsumerLoopCanceled(this ILogger logger);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Error,
            Message = "Unhandled error in Kafka consumer loop: {Message}")]
        public static partial void UnhandledErrorInLoop(this ILogger logger, string message, Exception exception);

        [LoggerMessage(
            EventId = 1009,
            Level = LogLevel.Warning,
            Message = "Attempted to close an already disposed consumer.")]
        public static partial void CloseAlreadyDisposedConsumer(this ILogger logger);

        [LoggerMessage(
            EventId = 1010,
            Level = LogLevel.Error,
            Message = "Error while closing consumer: {Message}")]
        public static partial void ErrorWhileClosingConsumer(this ILogger logger, string message, Exception exception);

        [LoggerMessage(
            EventId = 1011,
            Level = LogLevel.Error,
            Message = "Attempted to subscribe to topic '{Topic}' on a disposed Kafka consumer.")]
        public static partial void SubscribeOnDisposed(this ILogger logger, string topic);

        [LoggerMessage(
            EventId = 1012,
            Level = LogLevel.Information,
            Message = "Subscribed to topic: {Topic}, consumer {ConsumerId} topics: {SubscribedTopics}")]
        public static partial void SubscribedToTopic(this ILogger logger, string topic, string consumerId, string subscribedTopics);

        [LoggerMessage(
            EventId = 1013,
            Level = LogLevel.Information,
            Message = "The consumer {ConsumerId} has been unsubscribed. Related topics {SubscribedTopics}")]
        public static partial void UnsubscribedConsumer(this ILogger logger, string consumerId, string subscribedTopics);

        [LoggerMessage(
            EventId = 1014,
            Level = LogLevel.Warning,
            Message = "Kafka consumer already disposed. Skipping Close().")]
        public static partial void CloseSkippedDisposed(this ILogger logger);

        [LoggerMessage(
            EventId = 1015,
            Level = LogLevel.Warning,
            Message = "Kafka consumer already disposed. Skipping Dispose().")]
        public static partial void DisposeSkippedDisposed(this ILogger logger);

        [LoggerMessage(
            EventId = 1016,
            Level = LogLevel.Information,
            Message = "The consumer {ConsumerId} has been disposed. Related topics: {SubscribedTopics}")]
        public static partial void DisposedConsumer(this ILogger logger, string consumerId, string subscribedTopics);

        [LoggerMessage(
            EventId = 1017,
            Level = LogLevel.Error,
            Message = "Unexpected error checking consumer liveness")]
        public static partial void UnExpectedError(this ILogger logger, Exception exception);
    }
}
