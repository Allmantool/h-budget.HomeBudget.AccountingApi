using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static partial class KafkaPaymentsConsumerBackgroundServiceLogs
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Error,
            Message = "Shutting down {Service}...")]
        public static partial void OperationCanceled(
            this ILogger logger,
            string service,
            Exception exception);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Error,
            Message = "Unexpected error in {Service}. Restarting in {Delay} seconds...")]
        public static partial void UnexpectedError(
            this ILogger logger,
            string service,
            int delay,
            Exception exception);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Error,
            Message = "Failed to stop consumer")]
        public static partial void FailedToDisposeConsumer(
            this ILogger logger,
            Exception exception);
    }
}
