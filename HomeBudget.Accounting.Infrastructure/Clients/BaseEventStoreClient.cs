using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;

using HomeBudget.Core;
using HomeBudget.Core.Constants;

namespace EventStoreDbClient
{
    public abstract class BaseEventStoreClient<T> : IEventStoreDbClient<T>, IDisposable
        where T : class, new()
    {
        private readonly EventStoreClient _client;
        private readonly ILogger _logger;
        private bool _disposed;
        private static readonly ConcurrentDictionary<string, bool> SubscribedStreams = new();

        protected BaseEventStoreClient(EventStoreClient client, ILogger logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public virtual async Task<IWriteResult> SendAsync(
            T eventForSending,
            string streamName,
            string eventType,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            var eventData = CreateEventData(eventForSending, eventType);

            var writeStreamName = streamName ?? typeof(T).Name;

            var result = await _client.AppendToStreamAsync(
                writeStreamName,
                StreamState.Any,
                [eventData],
                cancellationToken: token);

            await EnsureSubscriptionAsync(writeStreamName, token);

            return result;
        }

        public virtual async Task<IWriteResult> SendBatchAsync(
            IEnumerable<T> eventsForSending,
            string streamName,
            string eventType = null,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(eventsForSending);

            var eventsToSend = eventsForSending
                .Select(e => CreateEventData(e, eventType))
                .ToList();

            if (eventsToSend.Count == 0)
            {
                throw new ArgumentException("Batch is empty", nameof(eventsForSending));
            }

            var writeStreamName = streamName ?? typeof(T).Name;

            var result = await _client.AppendToStreamAsync(
                writeStreamName,
                StreamState.Any,
                eventsToSend,
                cancellationToken: token);

            await EnsureSubscriptionAsync(writeStreamName, token);

            return result;
        }

        public virtual async IAsyncEnumerable<T> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var readState = _client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start, cancellationToken: cancellationToken);

            if (await readState.ReadState == ReadState.StreamNotFound)
            {
                yield break;
            }

            var count = 0;

            await foreach (var resolvedEvent in readState.WithCancellation(cancellationToken))
            {
                if (count >= maxEvents)
                {
                    yield break;
                }

                var evt = JsonSerializer.Deserialize<T>(resolvedEvent.Event.Data.Span);
                if (evt != null)
                {
                    count++;
                    yield return evt;
                }
            }
        }

        public virtual async Task SubscribeToStreamAsync(
            string streamName,
            Func<T, Task> onEventAppeared,
            CancellationToken cancellationToken = default)
        {
            await _client.SubscribeToStreamAsync(
                streamName,
                FromStream.Start,
                async (_, resolvedEvent, ct) =>
                {
                    var evt = JsonSerializer.Deserialize<T>(resolvedEvent.Event.Data.Span);
                    if (evt != null)
                    {
                        await onEventAppeared(evt);
                    }
                },
                cancellationToken: cancellationToken);
        }

        private static EventData CreateEventData(T @event, string eventType)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(@event);
            return new EventData(Uuid.NewUuid(), eventType ?? typeof(T).Name, bytes.AsMemory());
        }

        private async Task EnsureSubscriptionAsync(string streamName, CancellationToken token)
        {
            if (SubscribedStreams.ContainsKey(streamName))
            {
                return;
            }

            try
            {
                if (!SubscribedStreams.TryAdd(streamName, true))
                {
                    return;
                }

                var readState = _client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start, cancellationToken: token);
                if (await readState.ReadState != ReadState.StreamNotFound)
                {
                    await SubscribeToStreamAsync(streamName, OnEventAppearedAsync, token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring subscription for stream {StreamName}", streamName);
            }
        }

        protected virtual Task OnEventAppearedAsync(T eventData) => Task.CompletedTask;

        public async Task SendToDeadLetterQueueAsync(BaseEvent eventForSending, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            eventForSending.Metadata ??= [];
            eventForSending.Metadata[EventMetadataKeys.ExceptionDetails] = exception.Message;

            var data = CreateEventData((T)(object)eventForSending, "DeadLetter");

            await _client.AppendToStreamAsync(
                "DeadLetterStream",
                StreamState.Any,
                [data],
                cancellationToken: default);
        }

        public async Task SendToDeadLetterQueueAsync(
            IEnumerable<BaseEvent> eventsForSending,
            Exception exception)
        {
            foreach (var eventForSending in eventsForSending)
            {
                eventForSending.Metadata ??= [];
                eventForSending.Metadata[EventMetadataKeys.ExceptionDetails] = exception?.Message;
            }

            var data = eventsForSending.Select(ev => CreateEventData((T)(object)ev, "DeadLetter"));

            await _client.AppendToStreamAsync(
                "DeadLetterStream",
                StreamState.Any,
                data,
                cancellationToken: default);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
