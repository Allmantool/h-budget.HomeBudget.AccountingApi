using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Serilog.Context;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Logs;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Exstensions;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public class BaseEventStoreSubscriptionReadClient<T> : IEventStoreDbSubscriptionReadClient<T>, IDisposable
        where T : BaseEvent
    {
        private readonly ILogger _logger;
        private readonly EventStorePersistentSubscriptionsClient _client;
        private readonly EventStoreDbOptions _options;

        private bool _disposed;

        protected BaseEventStoreSubscriptionReadClient(
            EventStorePersistentSubscriptionsClient client,
            EventStoreDbOptions options,
            ILogger logger)
        {
            _client = client;
            _logger = logger;
            _options = options;
        }

        public virtual async Task CreatePersistentSubscriptionAsync(string groupName, CancellationToken ct)
        {
            try
            {
                var settings = new PersistentSubscriptionSettings(
                    resolveLinkTos: true,
                    startFrom: Position.Start);

                await _client.CreateToAllAsync(
                    groupName,
                    settings,
                    cancellationToken: ct);

                _logger.PersistentSubscriptionCreated(groupName);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                _logger.PersistentSubscriptionAlreadyExists(ex, groupName);
            }
            catch (Exception ex)
            {
                _logger.FailedCreateSubscription(ex, groupName);
            }
        }

        public virtual async Task<PersistentSubscription> SubscribeAsync(
            string groupName,
            Func<ResolvedEvent, Task> handler,
            CancellationToken ct = default)
        {
            var droppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PersistentSubscription subscription = null;

            try
            {
                subscription = await _client.SubscribeToAllAsync(
                    groupName,
                    async (sub, evt, retryCount, token) =>
                    {
                        var resolvedEvent = evt.Event;

                        if (resolvedEvent is null)
                        {
                            return;
                        }

                        if (resolvedEvent.EventType.StartsWith('$'))
                        {
                            await sub.Ack(evt);
                            return;
                        }

                        if (!resolvedEvent.EventStreamId.StartsWith(
                            $"{EventDbEventStreams.PaymentAccountPrefix}{NameConventions.EventPrefixSeparator}",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            await sub.Ack(evt);
                            return;
                        }

                        try
                        {
                            if (!SafeJsonSerializer.TryDeserialize<T>(resolvedEvent.Data.Span, out var eventData) || eventData is null)
                            {
                                await sub.Nack(
                                    PersistentSubscriptionNakEventAction.Skip,
                                    "Deserialization failed",
                                    evt);

                                return;
                            }

                            var correlationId = eventData.Metadata.Get(EventMetadataKeys.CorrelationId);
                            var traceParent = eventData.Metadata.Get(EventMetadataKeys.TraceParent);
                            var traceId = eventData.Metadata.Get(EventMetadataKeys.TraceId);

                            using (LogContext.PushProperty(EventMetadataKeys.CorrelationId, correlationId))
                            {
                                using var activity = Telemetry.ActivitySource.StartActivity(
                                    "eventstore.consume",
                                    ActivityKind.Consumer,
                                    traceParent); // Restore parent from EventStore

                                if (activity != null)
                                {
                                    activity.SetCorrelationId(correlationId);
                                    if (!string.IsNullOrWhiteSpace(traceId))
                                    {
                                        activity.SetTraceId(traceId);
                                    }

                                    activity.SetTag("messaging.system", "eventstore");
                                    activity.SetTag("messaging.stream", resolvedEvent.EventStreamId);
                                    activity.SetTag("messaging.event_type", resolvedEvent.EventType);
                                    activity.SetTag("messaging.event_id", resolvedEvent.EventId.ToString());
                                }

                                // Call the handler
                                if (handler is null)
                                {
                                    await OnEventAppearedAsync(eventData);
                                }
                                else
                                {
                                    await handler(evt);
                                }

                                await sub.Ack(evt);
                                activity?.SetStatus(ActivityStatusCode.Ok);
                                activity?.AddEvent(ActivityEvents.EventStorePersisted);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.HandlerFailedForEvent(ex, resolvedEvent.EventId);

                            await sub.Nack(
                                PersistentSubscriptionNakEventAction.Retry,
                                ex.Message,
                                evt);
                        }
                    },
                    (sub, reason, ex) =>
                    {
                        _logger.SubscriptionDropped(ex, reason);
                        droppedTcs.TrySetResult();
                    },
                    cancellationToken: ct);

                using (ct.Register(() => droppedTcs.TrySetCanceled(ct)))
                {
                    await droppedTcs.Task;
                }

                return subscription;
            }
            catch (Exception ex)
            {
                subscription?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual Task CreatePersistentSubscriptionAsync(CancellationToken ct) => Task.CompletedTask;

        public virtual Task<PersistentSubscription> SubscribeAsync(CancellationToken ct = default) => SubscribeAsync(null, ct);
        public virtual Task<PersistentSubscription> SubscribeAsync(
            Func<ResolvedEvent, Task> handler = null,
            CancellationToken ct = default) => Task.FromResult<PersistentSubscription>(null);

        protected virtual Task OnEventAppearedAsync(T eventData) => Task.CompletedTask;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
            {
                return;
            }

            _disposed = true;
        }
    }
}
