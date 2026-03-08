using System;
using System.Collections.Generic;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading;

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
                    context.Response.Headers.Add("Cache-Control", "no-store");
                    context.Response.Headers.Add("X-Accel-Buffering", "no");
                    context.Response.Headers.Add("Connection", "keep-alive");

                    return TypedResults.ServerSentEvents(StreamEventsAsync(notifications, lastEventId, ct));
                });

            return app;
        }

        private static async IAsyncEnumerable<SseItem<PaymentAccountNotification>> StreamEventsAsync(
            NotificationChannel notifications,
            string lastEventId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var heartbeatTimer = new PeriodicTimer(HeartbeatInterval);

            await foreach (var evt in notifications.ReadAsync(lastEventId, ct))
            {
                yield return new SseItem<PaymentAccountNotification>(evt, evt.EventType)
                {
                    EventId = evt.EventId,
                    ReconnectionInterval = TimeSpan.FromSeconds(2)
                };

                if (await heartbeatTimer.WaitForNextTickAsync(ct))
                {
                    yield return new SseItem<PaymentAccountNotification>(default!, "heartbeat");
                }
            }
        }
    }
}