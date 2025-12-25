using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Components.Operations.Logs
{
    internal static partial class BasePaymentCommandHandlerLogs
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Error,
            Message = "Kafka produce failed. Topic={Topic}, Key={Key}, Reason={Reason}")]
        public static partial void ProduceFailed(
            this ILogger logger,
            string topic,
            string key,
            string reason,
            Exception exception);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Error,
            Message = "CDC write failed. Table={Table}, Reason={Reason}")]
        public static partial void CdcWriteFailed(
            this ILogger logger,
            string table,
            string reason,
            Exception exception);
    }
}
