using System;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Components.Operations.Logs
{
    internal static partial class PaymentOperationsClientHandlerLogs
    {
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Error,
            Message = "Kafka producer error: {ErrorCode} - {Reason}")]
        public static partial void ProducerError(
            this ILogger logger,
            ErrorCode errorCode,
            string reason);

        [LoggerMessage(
            EventId = 3002,
            Level = LogLevel.Debug,
            Message = "Kafka producer stats: {Stats}")]
        public static partial void ProducerStats(
            this ILogger logger,
            string stats);

        [LoggerMessage(
            EventId = 3003,
            Level = LogLevel.Warning,
            Message = "Error flushing Kafka producer on dispose")]
        public static partial void ProducerFlushWarning(
            this ILogger logger,
            Exception exception);
    }
}
