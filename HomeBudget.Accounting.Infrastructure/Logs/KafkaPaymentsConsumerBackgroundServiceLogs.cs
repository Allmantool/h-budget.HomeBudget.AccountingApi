using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Logs
{
    internal static partial class KafkaPaymentsConsumerBackgroundServiceLogs
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Error,
            Message = "Shutting down {Service}...")]
        public static partial void OperationCanceled(
            ILogger logger,
            string service,
            Exception exception);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Error,
            Message = "Unexpected error in {Service}. Restarting in {Delay} seconds...")]
        public static partial void UnexpectedError(
            ILogger logger,
            string service,
            int delay,
            Exception exception);
    }
}
