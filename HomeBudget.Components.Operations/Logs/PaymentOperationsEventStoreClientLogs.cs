using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Components.Operations.Logs
{
    internal static partial class PaymentOperationsEventStoreWriteClientLogs
    {
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Information,
            Message = "Sending {EventType} for OperationKey={OperationKey}, CorrelationId={CorrelationId}")]
        public static partial void SendingEvent(this ILogger logger, string eventType, string operationKey, Guid correlationId);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "{EventType} sent: OperationKey={OperationKey}, CorrelationId={CorrelationId}")]
        public static partial void EventSent(this ILogger logger, string eventType, string operationKey, Guid correlationId);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Error,
            Message = "All retries exhausted for {EventType}. OperationKey={OperationKey}. Sending to DLQ.")]
        public static partial void RetriesExhausted(this ILogger logger, string eventType, string operationKey, Exception exception);

        [LoggerMessage(
            EventId = 1009,
            Level = LogLevel.Error,
            Message = "Payment event batch processor crashed. Send to dead queue: {Message}")]
        public static partial void SendEventToDeadQueue(this ILogger logger, string message, Exception exception);
    }
}
