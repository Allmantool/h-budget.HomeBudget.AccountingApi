using System;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Logs
{
    internal static partial class PaymentOperationsEventStoreReadClientLogs
    {
        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Information,
            Message = "Dispatching SyncOperationsHistoryCommand: AccountId={AccountId}, Events={Count}")]
        public static partial void DispatchingSync(this ILogger logger, Guid accountId, int count);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Error,
            Message = "SyncOperationsHistoryCommand failed for AccountId={AccountId}, stream={Stream}.")]
        public static partial void SyncFailed(this ILogger logger, Guid accountId, string stream, Exception exception);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Error,
            Message = "Failed to handle events for PeriodKey={PeriodKey}.")]
        public static partial void HandleEventsFailed(this ILogger logger, string periodKey, Exception exception);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Debug,
            Message = "Channel write canceled.")]
        public static partial void ChannelWriteCanceled(this ILogger logger);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Warning,
            Message = "Channel closed: dropping event {EventType}.")]
        public static partial void ChannelClosedDropping(this ILogger logger, string eventType);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Error,
            Message = "Payment event batch processor crashed.")]
        public static partial void BatchProcessorCrashed(this ILogger logger, Exception exception);
    }
}
