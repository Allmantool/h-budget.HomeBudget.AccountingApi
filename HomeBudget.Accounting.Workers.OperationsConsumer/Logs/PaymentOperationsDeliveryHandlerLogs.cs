using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static partial class PaymentOperationsDeliveryHandlerLogs
    {
        [LoggerMessage(
            EventId = 1026,
            Level = LogLevel.Error,
            Message = "Failed to send events to stream {StreamName}: {Message}")]
        public static partial void FailedToSendEventToStream(
            this ILogger logger,
            Exception exception,
            string streamName,
            string message);

        [LoggerMessage(
            EventId = 1024,
            Level = LogLevel.Error,
            Message = "{Handler} failed to process payment events: {Message}")]
        public static partial void FailedToProccessEvent(
            this ILogger logger,
            Exception exception,
            string handler,
            string message);
    }
}
