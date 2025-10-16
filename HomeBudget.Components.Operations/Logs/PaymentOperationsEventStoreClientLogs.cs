using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Components.Operations.Logs
{
    internal static partial class PaymentOperationsEventStoreClientLogs
    {
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Information,
            Message = "Sending {EventType} for OperationKey={OperationKey}, CorrelationId={CorrelationId}")]
        public static partial void SendingEvent(ILogger logger, string eventType, string operationKey, Guid correlationId);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "{EventType} sent: OperationKey={OperationKey}, CorrelationId={CorrelationId}")]
        public static partial void EventSent(ILogger logger, string eventType, string operationKey, Guid correlationId);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Error,
            Message = "All retries exhausted for {EventType}. OperationKey={OperationKey}. Sending to DLQ.")]
        public static partial void RetriesExhausted(ILogger logger, string eventType, string operationKey, Exception exception);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Warning,
            Message = "Channel closed: dropping event {EventType}.")]
        public static partial void ChannelClosedDropping(ILogger logger, string eventType);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Debug,
            Message = "Channel write canceled.")]
        public static partial void ChannelWriteCanceled(ILogger logger);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Error,
            Message = "Failed to handle events for PeriodKey={PeriodKey}.")]
        public static partial void HandleEventsFailed(ILogger logger, string periodKey, Exception exception);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Error,
            Message = "SyncOperationsHistoryCommand failed for AccountId={AccountId}, PeriodKey={PeriodKey}.")]
        public static partial void SyncFailed(ILogger logger, Guid accountId, string periodKey, Exception exception);

        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Information,
            Message = "Dispatching SyncOperationsHistoryCommand: AccountId={AccountId}, Events={Count}")]
        public static partial void DispatchingSync(ILogger logger, Guid accountId, int count);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Error,
            Message = "Payment event batch processor crashed.")]
        public static partial void BatchProcessorCrashed(ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 1009,
            Level = LogLevel.Error,
            Message = "Payment event batch processor crashed. Send to dead queue: {Message}")]
        public static partial void SendEventToDeadQueue(ILogger logger, string message, Exception exception);
    }
}
