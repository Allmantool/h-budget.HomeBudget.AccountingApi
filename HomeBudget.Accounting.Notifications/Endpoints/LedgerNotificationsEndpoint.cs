using System;
using System.Collections.Generic;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Endpoints
{
    public static class LedgerNotificationsEndpoint
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

        public static IEndpointRouteBuilder MapOperationNotifications(this IEndpointRouteBuilder app)
        {
            app.MapGet(
                "/notifications/account",
                (
                 [FromServices] NotificationChannel notifications,
                 [FromHeader(Name = "Last-Event-ID")] string lastEventId,
                 HttpContext context,
                 CancellationToken ct) =>
                {
                    context.Response.Headers.Append("Cache-Control", "no-store");
                    context.Response.Headers.Append("X-Accel-Buffering", "no");
                    context.Response.Headers.Append("Connection", "keep-alive");

                    return TypedResults.ServerSentEvents(StreamEventsAsync(notifications, lastEventId, ct));
                });

            return app;
        }

        private static async IAsyncEnumerable<SseItem<PaymentAccountNotification>> StreamEventsAsync(
            NotificationChannel notifications,
            string? lastEventId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var heartbeatTimer = new PeriodicTimer(HeartbeatInterval);

            var events = notifications.ReadAsync(lastEventId, ct).GetAsyncEnumerator(ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var moveNextTask = events.MoveNextAsync().AsTask();
                    var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();

                    var completed = await Task.WhenAny(moveNextTask, heartbeatTask);

                    if (completed == moveNextTask && await moveNextTask)
                    {
                        var evt = events.Current;

                        if (evt is null)
                        {
                            continue;
                        }

                        yield return new SseItem<PaymentAccountNotification>(
                            evt,
                            evt.EventType ?? "notification")
                        {
                            EventId = evt.EventId ?? Guid.NewGuid().ToString("N"),
                            ReconnectionInterval = TimeSpan.FromSeconds(2)
                        };
                    }
                    else
                    {
                        yield return new SseItem<PaymentAccountNotification>(
                            new PaymentAccountNotification(
                                Guid.Empty.ToString(),
                                "heartbeat",
                                Guid.Empty),
                            "heartbeat");
                    }
                }
            }
            finally
            {
                await events.DisposeAsync();
            }
        }
    }
}