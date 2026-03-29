using System;
using System.Collections.Generic;
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

                            MergeMetadata(resolvedEvent.Metadata.Span, eventData);

                            var correlationId = eventData.Metadata.Get(EventMetadataKeys.CorrelationId);
                            var traceParent = eventData.Metadata.Get(EventMetadataKeys.TraceParent);
                            var traceState = eventData.Metadata.Get(EventMetadataKeys.TraceState);
                            var baggage = eventData.Metadata.Get(EventMetadataKeys.Baggage);
                            var messageId = eventData.Metadata.Get(EventMetadataKeys.MessageId);
                            var causationId = eventData.Metadata.Get(EventMetadataKeys.CausationId);
                            var propagationContext = TraceContextPropagation.Extract(
                                TraceContextPropagation.BuildCarrier(traceParent, traceState, baggage));

                            using (LogContext.PushProperty(EventMetadataKeys.CorrelationId, correlationId))
                            using (LogContext.PushProperty(EventMetadataKeys.MessageId, messageId))
                            using (LogContext.PushProperty(EventMetadataKeys.CausationId, causationId))
                            using (LogContext.PushProperty("stream_id", resolvedEvent.EventStreamId))
                            using (LogContext.PushProperty("event_type", resolvedEvent.EventType))
                            using (LogContext.PushProperty("retry_count", retryCount))
                            {
                                var retryAttempt = retryCount ?? 0;
                                using var activity = ActivityPropagation.StartActivity(
                                    "eventstore.consume",
                                    ActivityKind.Consumer,
                                    propagationContext);
                                using var baggageScope = TraceContextPropagation.UseExtractedBaggage(propagationContext);
                                var consumeStopwatch = Stopwatch.StartNew();

                                if (activity != null)
                                {
                                    activity.SetCorrelationId(correlationId);
                                    activity.SetTag("messaging.system", "eventstore");
                                    activity.SetTag("messaging.stream", resolvedEvent.EventStreamId);
                                    activity.SetTag("messaging.event_type", resolvedEvent.EventType);
                                    activity.SetTag("messaging.event_id", resolvedEvent.EventId.ToString());
                                    activity.SetTag("messaging.message_id", messageId);
                                    activity.SetTag("messaging.conversation_id", causationId);
                                    activity.SetTag("messaging.retry.count", retryAttempt);
                                }

                                if (retryAttempt > 0)
                                {
                                    TelemetryMetrics.EventStoreRetries.Add(
                                        1,
                                        [new KeyValuePair<string, object>("event_type", resolvedEvent.EventType)]);
                                    activity?.AddEvent(ActivityEvents.RetryAttempt(retryAttempt));
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
                                consumeStopwatch.Stop();
                                TelemetryMetrics.EventStoreConsumeDurationMs.Record(
                                    consumeStopwatch.Elapsed.TotalMilliseconds,
                                    [new KeyValuePair<string, object>("event_type", resolvedEvent.EventType)]);
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

        private static void MergeMetadata(ReadOnlySpan<byte> metadataBytes, T target)
        {
            if (metadataBytes.IsEmpty || target is not BaseEvent baseEvent)
            {
                return;
            }

            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataBytes);
            if (metadata is null)
            {
                return;
            }

            foreach (var item in metadata)
            {
                baseEvent.Metadata[item.Key] = item.Value;
            }
        }

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
