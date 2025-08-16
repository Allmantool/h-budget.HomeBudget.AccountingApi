using System;

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations
{
    internal static class EventStoreRetryPolicies
    {
        public static AsyncRetryPolicy BuildRetryPolicy(EventStoreDbOptions opts, ILogger logger)
        {
            var retryCount = Math.Max(0, opts.RetryAttempts);
            var rise = Math.Max(1, opts.RetryRiseNumber);

            return Policy
                .Handle<RpcException>(ex =>
                    ex.StatusCode == StatusCode.DeadlineExceeded ||
                    ex.StatusCode == StatusCode.Unavailable ||
                    ex.StatusCode == StatusCode.Cancelled ||
                    ex.StatusCode == StatusCode.ResourceExhausted ||
                    ex.StatusCode == StatusCode.Internal)
                .WaitAndRetryAsync(
                    retryCount: retryCount,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(rise, attempt)),
                    onRetry: (exception, delay, attempt, context) =>
                    {
                        var eventType = context.TryGetValue(nameof(PaymentOperationEvent.EventType), out var et)
                            ? et as string
                            : null;
                        var key = context.TryGetValue(nameof(PaymentOperationEvent.Payload.Key), out var k)
                            ? k?.ToString()
                            : null;

                        logger.LogWarning(
                            exception,
                            "Retry {Attempt} after {Delay}. EventType={EventType}, EventKey={EventKey}",
                            attempt,
                            delay,
                            eventType,
                            key);
                    });
        }
    }
}
