using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Logs
{
    internal static partial class BaseEventStoreClientLogs
    {
        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Error,
            Message = "Error ensuring subscription for stream {StreamName}")]
        public static partial void SubscriptionError(ILogger logger, string streamName, Exception ex);
    }
}
