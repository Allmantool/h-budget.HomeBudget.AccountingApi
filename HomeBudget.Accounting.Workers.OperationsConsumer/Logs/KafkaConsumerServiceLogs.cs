using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static partial class KafkaConsumerServiceLogs
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "Consume loop has been stopped.")]
        public static partial void ConsumeLoopStopped(this ILogger logger);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Trace,
            Message = "No active consumers. Waiting for new topics...")]
        public static partial void NoActiveConsumers(this ILogger logger);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Information,
            Message = "Consume loop has been cancelled.")]
        public static partial void ConsumeLoopCancelled(this ILogger logger);

        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Error,
            Message = "Error while consuming Kafka messages.")]
        public static partial void ErrorConsumingMessages(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2005,
            Level = LogLevel.Information,
            Message = "Subscribed to topic {Title}, consumer type {ConsumerType}")]
        public static partial void SubscribedToTopic(this ILogger logger, string title, string consumerType);

        [LoggerMessage(
            EventId = 2006,
            Level = LogLevel.Information,
            Message = "Consume loop has been started.")]
        public static partial void ConsumeLoopStarted(this ILogger logger);

        [LoggerMessage(
            EventId = 2007,
            Level = LogLevel.Error,
            Message = "Not fatal kafka error.")]
        public static partial void NonFatalConsumeError(this ILogger logger, Exception exception);
    }
}
