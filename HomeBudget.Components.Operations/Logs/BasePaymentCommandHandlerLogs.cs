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
            ILogger logger,
            string topic,
            string key,
            string reason,
            Exception exception);
    }
}
