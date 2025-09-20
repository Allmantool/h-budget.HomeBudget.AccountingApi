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
    }
}
