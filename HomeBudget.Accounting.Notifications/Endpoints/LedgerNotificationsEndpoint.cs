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
                    [FromServices] NotificationChannel notifications,
                    CancellationToken ct) =>
                {
                    async IAsyncEnumerable<SseItem<PaymentAccountNotification>> Stream(
                        [EnumeratorCancellation] CancellationToken token)
                    {
                        var heartbeatTimer = new PeriodicTimer(HeartbeatInterval);

                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                var readTask = notifications.ReadAsync(token).AsTask();
                                var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(token).AsTask();

                                var completed = await Task.WhenAny(readTask, heartbeatTask);

                                if (completed == readTask)
                                {
                                    var evt = await readTask;

                                    yield return new SseItem<PaymentAccountNotification>(
                                        evt,
                                        evt.EventType)
                                    {
                                        EventId = evt.EventId,
                                        ReconnectionInterval = TimeSpan.FromSeconds(2)
                                    };
                                }
                                else
                                {
                                    yield return new SseItem<PaymentAccountNotification>(
                                        default!,
                                        "heartbeat");
                                }
                            }
                        }
                        finally
                        {
                            heartbeatTimer.Dispose();
                        }
                    }

                    return TypedResults.ServerSentEvents(Stream(ct));
                });

            return app;
        }
    }
}