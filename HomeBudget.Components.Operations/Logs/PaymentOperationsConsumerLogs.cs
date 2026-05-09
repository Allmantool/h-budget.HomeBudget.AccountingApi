using System;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Components.Operations.Logs
{
    internal static partial class PaymentOperationsConsumerLogs
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "Payment operation message has been consumed: {MessageId}")]
        public static partial void PaymentConsumed(this ILogger logger, string messageId);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Error,
            Message = "Failed to deserialize message: {Message}. Error: {Error}")]
        public static partial void DeserializationFailed(this ILogger logger, string? message, string error, JsonException ex);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Error,
            Message = "{Consumer} failed to consume with error: {Exception}")]
        public static partial void ConsumerFailed(this ILogger logger, string consumer, string exception, Exception ex);

        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Information,
            Message = "Duplicate payment operation message skipped. MessageId={MessageId}, Status={Status}")]
        public static partial void DuplicateMessageSkipped(this ILogger logger, string messageId, string status);

        [LoggerMessage(
            EventId = 2005,
            Level = LogLevel.Warning,
            Message = "Payment operation message failed transiently and will be retried. MessageId={MessageId}, RetryCount={RetryCount}, Error={Error}")]
        public static partial void TransientMessageFailure(this ILogger logger, string messageId, int retryCount, string error, Exception ex);

        [LoggerMessage(
            EventId = 2006,
            Level = LogLevel.Error,
            Message = "Payment operation message reached poison/DLQ state. MessageId={MessageId}, RetryCount={RetryCount}, Error={Error}")]
        public static partial void PoisonMessageReachedDeadLetter(this ILogger logger, string messageId, int retryCount, string error, Exception ex);
    }
}
