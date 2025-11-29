using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Logs
{
    internal static partial class KafkaConsumerHealthMonitorBackgroundServiceLogs
    {
        [LoggerMessage(
            EventId = 0,
            Level = LogLevel.Information,
            Message = "Running Kafka consumer health check...")]
        public static partial void RunningKafkaConsumerHealthCheck(this ILogger logger);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Error,
            Message = "Error during Kafka consumer health check. Retrying after delay...")]
        public static partial void ErrorConsumerMonitoringMessages(this ILogger logger, Exception exception);
    }
}
