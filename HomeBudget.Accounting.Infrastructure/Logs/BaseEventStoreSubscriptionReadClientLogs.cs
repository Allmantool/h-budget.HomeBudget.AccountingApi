using System;

using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Logs
{
    public static partial class BaseEventStoreSubscriptionReadClientLogs
    {
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Information,
            Message = "Persistent subscription '{Group}' created on $all")]
        public static partial void PersistentSubscriptionCreated(this ILogger logger, string group);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Persistent subscription '{Group}' already exists")]
        public static partial void PersistentSubscriptionAlreadyExists(this ILogger logger, RpcException ex, string group);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Error,
            Message = "Failed to create subscription '{Group}' with error")]
        public static partial void FailedCreateSubscription(this ILogger logger, Exception ex, string group);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Error,
            Message = "Handler failed for event {EventId}")]
        public static partial void HandlerFailedForEvent(this ILogger logger, Exception ex, Uuid eventId);
    }
}
