using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                    startFrom: _options.PaymentHistoryProjectionStartFromCurrent
                        ? Position.End
                        : Position.Start);

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
                throw;
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
                            ApplyEventStorePosition(resolvedEvent, eventData);

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
                                    var subscriptionContext = new EventStoreSubscriptionContext
                                    {
                                        StreamId = resolvedEvent.EventStreamId,
                                        Revision = resolvedEvent.EventNumber.ToString(),
                                        Position = resolvedEvent.Position.ToString(),
                                        Acknowledge = () => sub.Ack(evt),
                                        Retry = reason => sub.Nack(
                                            PersistentSubscriptionNakEventAction.Retry,
                                            reason,
                                            evt)
                                    };

                                    await OnEventAppearedAsync(
                                        eventData,
                                        subscriptionContext);

                                    if (!DefersAcknowledgement)
                                    {
                                        await subscriptionContext.AcknowledgeAsync();
                                    }
                                }
                                else
                                {
                                    await handler(evt);
                                    await sub.Ack(evt);
                                }
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
                    bufferSize: Math.Max(1, _options.EventProcessingBatchSize),
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

        protected async Task SubscribeStreamingAsync(string groupName, CancellationToken ct)
        {
            await using var subscription = _client.SubscribeToAll(
                groupName,
                bufferSize: Math.Max(1, _options.EventProcessingBatchSize),
                cancellationToken: ct);

            await using var enumerator = subscription.GetAsyncEnumerator(ct);
            Task<bool> pendingMove = null;
            var endOfSubscription = false;
            var batchSize = Math.Max(1, _options.EventProcessingBatchSize);
            var batchDelay = TimeSpan.FromMilliseconds(Math.Max(1, _options.EventBatchingDelayInMs));

            while (!endOfSubscription)
            {
                var batch = new List<StreamingEvent>(batchSize);
                var flushAt = DateTime.MaxValue;

                while (batch.Count < batchSize && !endOfSubscription)
                {
                    pendingMove ??= enumerator.MoveNextAsync().AsTask();
                    if (batch.Count > 0)
                    {
                        var remaining = flushAt - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero ||
                            await Task.WhenAny(pendingMove, Task.Delay(remaining, ct)) != pendingMove)
                        {
                            break;
                        }
                    }

                    var hasNext = await pendingMove;
                    pendingMove = null;
                    if (!hasNext)
                    {
                        endOfSubscription = true;
                        break;
                    }

                    var evt = enumerator.Current;
                    var resolvedEvent = evt.Event;
                    if (resolvedEvent is null)
                    {
                        continue;
                    }

                    if (resolvedEvent.EventType.StartsWith('$') ||
                        !resolvedEvent.EventStreamId.StartsWith(
                            $"{EventDbEventStreams.PaymentAccountPrefix}{NameConventions.EventPrefixSeparator}",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await subscription.Ack(evt);
                        continue;
                    }

                    if (!SafeJsonSerializer.TryDeserialize<T>(resolvedEvent.Data.Span, out var eventData) || eventData is null)
                    {
                        await subscription.Nack(
                            PersistentSubscriptionNakEventAction.Skip,
                            "Deserialization failed",
                            evt);
                        continue;
                    }

                    MergeMetadata(resolvedEvent.Metadata.Span, eventData);
                    ApplyEventStorePosition(resolvedEvent, eventData);
                    batch.Add(new StreamingEvent(
                        eventData,
                        new EventStoreSubscriptionContext
                        {
                            StreamId = resolvedEvent.EventStreamId,
                            Revision = resolvedEvent.EventNumber.ToString(),
                            Position = resolvedEvent.Position.ToString()
                        },
                        evt));

                    if (batch.Count == 1)
                    {
                        flushAt = DateTime.UtcNow + batchDelay;
                    }
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                var resolvedEvents = batch.Select(static item => item.ResolvedEvent).ToArray();
                try
                {
                    await OnEventBatchAppearedAsync(
                        batch.Select(static item => (item.EventData, item.Context)).ToArray());
                    await subscription.Ack(resolvedEvents);
                }
                catch (Exception ex)
                {
                    _logger.HandlerFailedForEvent(ex, batch[0].ResolvedEvent.Event.EventId);
                    await subscription.Nack(
                        PersistentSubscriptionNakEventAction.Retry,
                        ex.Message,
                        resolvedEvents);
                }
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

        protected virtual Task OnEventAppearedAsync(T eventData, EventStoreSubscriptionContext context)
            => OnEventAppearedAsync(eventData);

        protected virtual async Task OnEventBatchAppearedAsync(
            IReadOnlyCollection<(T EventData, EventStoreSubscriptionContext Context)> events)
        {
            foreach (var (eventData, context) in events)
            {
                await OnEventAppearedAsync(eventData, context);
            }
        }

        protected virtual bool DefersAcknowledgement => false;

        private sealed record StreamingEvent(
            T EventData,
            EventStoreSubscriptionContext Context,
            ResolvedEvent ResolvedEvent);

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

        private static void ApplyEventStorePosition(EventRecord resolvedEvent, T target)
        {
            if (target is not BaseEvent baseEvent)
            {
                return;
            }

            if (long.TryParse(resolvedEvent.EventNumber.ToString(), out var sequenceNumber))
            {
                baseEvent.SequenceNumber = sequenceNumber;
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
