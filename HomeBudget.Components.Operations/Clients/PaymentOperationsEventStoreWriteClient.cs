using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal sealed class PaymentOperationsEventStoreWriteClient
        : BaseEventStoreWriteClient<PaymentOperationEvent>, IDisposable
    {
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<PaymentOperationsEventStoreWriteClient> _logger;
        private readonly EventStoreDbOptions _opts;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> StreamLocks = new();
        private static readonly ConcurrentDictionary<string, PaymentStreamCache> StreamCaches = new();

        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _requestRateLimiter;

        public PaymentOperationsEventStoreWriteClient(
            ILogger<PaymentOperationsEventStoreWriteClient> logger,
            EventStoreClient client,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
            _opts = options.Value;
            _logger = logger;
            _retryPolicy = EventStoreRetryPolicies.BuildRetryPolicy(_opts, _logger);
            _requestRateLimiter = new(_opts.RequestRateLimiter);
        }

        public override async Task<IWriteResult> SendBatchAsync(
            IEnumerable<PaymentOperationEvent> eventsForSending,
            string streamName,
            string eventType = null,
            CancellationToken ctx = default)
        {
            ArgumentNullException.ThrowIfNull(eventsForSending);

            var events = eventsForSending.ToList();
            if (events.Count == 0)
            {
                throw new ArgumentException("Batch is empty", nameof(eventsForSending));
            }

            try
            {
                await _requestRateLimiter.WaitAsync(ctx);

                IWriteResult result = null;
                foreach (var paymentEvent in events)
                {
                    result = await SendIdempotentAsync(paymentEvent, streamName, eventType, ctx);
                }

                return result;
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreWriteClientLogs.SendEventToDeadQueue(_logger, ex.Message, ex);
                await SendToDeadLetterQueueAsync(events, ex);
                throw;
            }
            finally
            {
                _requestRateLimiter.Release();
            }
        }

        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            try
            {
                return await SendIdempotentAsync(eventForSending, streamName, eventType, token);
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreWriteClientLogs.RetriesExhausted(_logger, eventType, eventForSending.Payload?.Key.ToString(), ex);
                await SendToDeadLetterQueueAsync(eventForSending, ex);
                throw;
            }
        }

        private async Task<IWriteResult> SendIdempotentAsync(
            PaymentOperationEvent eventForSending,
            string streamName,
            string eventType,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            PaymentOperationEventIdentity.EnsureMetadata(eventForSending);

            var writeStreamName = streamName ?? nameof(PaymentOperationEvent);
            var writeEventType = eventType ?? eventForSending.EventType.ToString();
            var eventId = PaymentOperationEventIdentity.GetDeterministicEventId(eventForSending, writeEventType);
            var streamLock = StreamLocks.GetOrAdd(writeStreamName, _ => new SemaphoreSlim(1, 1));
            var streamCache = StreamCaches.GetOrAdd(writeStreamName, _ => new PaymentStreamCache());

            await streamLock.WaitAsync(token);
            try
            {
                return await _retryPolicy.ExecuteAsync(
                    async retryCtx =>
                    {
                        eventForSending.Metadata[EventDbEventMetadataKeys.RetryCount] = retryCtx.Count.ToString(CultureInfo.InvariantCulture);

                        var correlationId = eventForSending.Metadata.Get(EventMetadataKeys.CorrelationId);
                        if (Guid.TryParse(correlationId, out var envelopeId))
                        {
                            eventForSending.EnvelopId = envelopeId;
                        }

                        var opKey = eventForSending.Payload?.Key.ToString();
                        PaymentOperationsEventStoreWriteClientLogs.SendingEvent(_logger, writeEventType, opKey, retryCtx.CorrelationId);

                        var result = await AppendWithExpectedRevisionAsync(
                            eventForSending,
                            writeStreamName,
                            writeEventType,
                            eventId,
                            streamCache,
                            token);

                        PaymentOperationsEventStoreWriteClientLogs.EventSent(_logger, writeEventType, opKey, retryCtx.CorrelationId);
                        return result;
                    },
                    new Context
                    {
                        [nameof(PaymentOperationEvent.EventType)] = writeEventType,
                        [nameof(PaymentOperationEvent.Payload.Key)] = eventForSending.Payload?.Key
                    });
            }
            finally
            {
                streamLock.Release();
            }
        }

        private async Task<IWriteResult> AppendWithExpectedRevisionAsync(
            PaymentOperationEvent eventForSending,
            string streamName,
            string eventType,
            Uuid eventId,
            PaymentStreamCache streamCache,
            CancellationToken token)
        {
            while (true)
            {
                var streamState = await ReadStreamStateAsync(streamName, eventId, streamCache, token);
                if (streamState.DuplicateResult != null)
                {
                    return streamState.DuplicateResult;
                }

                var eventData = CreateEventData(eventForSending, eventType, eventId);

                try
                {
                    var writeResult = streamState.ExpectedRevision.HasValue
                        ? await Client.AppendToStreamAsync(
                            streamName,
                            streamState.ExpectedRevision.Value,
                            [eventData],
                            cancellationToken: token)
                        : await Client.AppendToStreamAsync(
                            streamName,
                            StreamState.NoStream,
                            [eventData],
                            cancellationToken: token);

                    streamCache.EventIds.Add(eventId);
                    streamCache.LatestRevision = writeResult.NextExpectedStreamRevision;
                    streamCache.LatestPosition = writeResult.LogPosition;
                    streamCache.IsInitialized = true;

                    return writeResult;
                }
                catch (WrongExpectedVersionException)
                {
                    streamCache.IsInitialized = false;
                    var latestState = await ReadStreamStateAsync(streamName, eventId, streamCache, token);
                    if (latestState.DuplicateResult != null)
                    {
                        return latestState.DuplicateResult;
                    }

                    continue;
                }
            }
        }

        private async Task<PaymentStreamState> ReadStreamStateAsync(
            string streamName,
            Uuid eventId,
            PaymentStreamCache streamCache,
            CancellationToken token)
        {
            if (streamCache.IsInitialized)
            {
                return streamCache.EventIds.Contains(eventId)
                    ? PaymentStreamState.Duplicate(
                        new IdempotentWriteResult(streamCache.LatestRevision ?? StreamRevision.None, streamCache.LatestPosition))
                    : new PaymentStreamState(streamCache.LatestRevision, null);
            }

            var readResult = Client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.Start,
                cancellationToken: token);

            if (await readResult.ReadState == ReadState.StreamNotFound)
            {
                streamCache.IsInitialized = true;
                return PaymentStreamState.Empty;
            }

            StreamRevision? latestRevision = null;
            var latestPosition = Position.Start;
            streamCache.EventIds.Clear();

            await foreach (var resolvedEvent in readResult.WithCancellation(token))
            {
                latestRevision = StreamRevision.FromStreamPosition(resolvedEvent.Event.EventNumber);
                latestPosition = resolvedEvent.Event.Position;
                streamCache.EventIds.Add(resolvedEvent.Event.EventId);
                if (resolvedEvent.Event.EventId == eventId)
                {
                    streamCache.LatestRevision = latestRevision;
                    streamCache.LatestPosition = latestPosition;
                    streamCache.IsInitialized = true;

                    return PaymentStreamState.Duplicate(
                        new IdempotentWriteResult(latestRevision.Value, latestPosition));
                }
            }

            streamCache.LatestRevision = latestRevision;
            streamCache.LatestPosition = latestPosition;
            streamCache.IsInitialized = true;

            return new PaymentStreamState(latestRevision, null);
        }

        private sealed class PaymentStreamCache
        {
            public HashSet<Uuid> EventIds { get; } = [];

            public bool IsInitialized { get; set; }

            public StreamRevision? LatestRevision { get; set; }

            public Position LatestPosition { get; set; } = Position.Start;
        }

        private sealed class PaymentStreamState
        {
            public PaymentStreamState(
                StreamRevision? expectedRevision,
                IWriteResult duplicateResult)
            {
                ExpectedRevision = expectedRevision;
                DuplicateResult = duplicateResult;
            }

            public StreamRevision? ExpectedRevision { get; }

            public IWriteResult DuplicateResult { get; }

            public static PaymentStreamState Empty { get; } = new(null, null);

            public static PaymentStreamState Duplicate(IWriteResult result) => new(result.NextExpectedStreamRevision, result);
        }

        public override async Task SendToDeadLetterQueueAsync(BaseEvent eventForSending, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            var ctx = new Context
            {
                [nameof(PaymentOperationEvent.EventType)] = EventDbEventTypes.DeadLetter,
                [nameof(PaymentOperationEvent.Payload.Key)] = eventForSending.EnvelopId
            };

            await _retryPolicy.ExecuteAsync(
                async _ => await base.SendToDeadLetterQueueAsync(eventForSending, exception),
                ctx);
        }

        public override async Task SendToDeadLetterQueueAsync(
            IEnumerable<BaseEvent> eventsForSending,
            Exception exception)
        {
            ArgumentNullException.ThrowIfNull(eventsForSending);

            var events = eventsForSending.ToList();
            var ctx = new Context
            {
                [nameof(PaymentOperationEvent.EventType)] = EventDbEventTypes.DeadLetter,
                [nameof(PaymentOperationEvent.Payload.Key)] = events.FirstOrDefault()?.EnvelopId
            };

            await _retryPolicy.ExecuteAsync(
                async _ => await base.SendToDeadLetterQueueAsync(events, exception),
                ctx);
        }

        public void Dispose()
        {
            _cts.Cancel();

            try
            {
                _requestRateLimiter.Dispose();

                base.Dispose();
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }
}
