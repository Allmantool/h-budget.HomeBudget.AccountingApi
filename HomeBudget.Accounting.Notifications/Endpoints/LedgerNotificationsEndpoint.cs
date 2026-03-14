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
using HomeBudget.Accounting.Notifications.Services;

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
                 [FromServices] INotificationChannel notifications,
                 [FromHeader(Name = "Last-Event-ID")] string lastEventId,
                 [FromQuery(Name = "lastEventId")] string lastEventIdQuery,
                 HttpContext context,
                 CancellationToken ct) =>
                {
                    context.Response.Headers.Append("Cache-Control", "no-cache, no-store, no-transform");
                    context.Response.Headers.Append("Pragma", "no-cache");
                    context.Response.Headers.Append("X-Accel-Buffering", "no");
                    context.Response.Headers.Append("Content-Encoding", "identity");

                    return TypedResults.ServerSentEvents(StreamEventsAsync(
                        notifications,
                        string.IsNullOrWhiteSpace(lastEventId) ? lastEventIdQuery : lastEventId,
                        ct));
                });

            return app;
        }

        private static async IAsyncEnumerable<SseItem<PaymentAccountNotification>> StreamEventsAsync(
            INotificationChannel notifications,
            string lastEventId,
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

                        yield return new SseItem<PaymentAccountNotification>(evt)
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
