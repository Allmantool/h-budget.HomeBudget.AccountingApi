using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static partial class KafkaPaymentsConsumerWorkerLogs
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "Creating Kafka consumer...")]
        public static partial void CreateKafkaConsumer(this ILogger logger);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Error,
            Message = "Kafka error. Recreating consumer after delay.")]
        public static partial void RecreateConsumerAfterDelay(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Error,
            Message = "Unexpected consumer error. Restarting consumer.")]
        public static partial void RestartingConsumer(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Error,
            Message = "Subscription failed, retrying...")]
        public static partial void SubscriptionFailed(this ILogger logger, Exception ex);
    }
}
