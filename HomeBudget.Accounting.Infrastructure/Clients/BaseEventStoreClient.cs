using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class BaseEventStoreClient<T>(EventStoreClient client, EventStoreDbOptions options, ILogger logger)
        : IEventStoreDbClient<T>, IDisposable
        where T : new()
    {
        private bool _disposed;
        private static readonly ConcurrentDictionary<string, bool> AlreadySubscribedStreams = new();
        private readonly SemaphoreSlim _subscriptionLock = new(1, 150);

        public virtual async Task<IWriteResult> SendAsync(
            T eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            await _subscriptionLock.WaitAsync(token);
            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(eventForSending);

            var eventData = new EventData(
                Uuid.NewUuid(),
                eventType ?? $"{nameof(T)}_{utf8Bytes.Length}",
                utf8Bytes.AsMemory());

            var writeStreamName = streamName ?? nameof(T);

            var writeResult = await client
                .AppendToStreamAsync(
                    writeStreamName,
                    StreamState.Any,
                    [eventData],
                    cancellationToken: token);

            await EnsureSubscriptionAsync(writeStreamName, token);

            return writeResult;
        }

        public virtual async IAsyncEnumerable<T> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var eventsAsyncStream = client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.Start,
                cancellationToken: cancellationToken
            );

            if (await eventsAsyncStream.ReadState == ReadState.StreamNotFound)
            {
                yield return default;
            }

            var eventsRead = 0;

            await foreach (var paymentOperationEvent in eventsAsyncStream)
            {
                if (eventsRead >= maxEvents)
                {
                    yield break;
                }

                var eventPayloadAsBytes = paymentOperationEvent.Event.Data.ToArray();
                using var eventDataStream = new MemoryStream(eventPayloadAsBytes);

                var deserializationResult = await JsonSerializer.DeserializeAsync<T>(eventDataStream, cancellationToken: cancellationToken);

                if (deserializationResult == null)
                {
                    continue;
                }

                eventsRead++;

                yield return deserializationResult;
            }
        }

        public virtual async Task SubscribeToStreamAsync(
            string streamName,
            Func<T, Task> onEventAppeared,
            CancellationToken cancellationToken = default)
        {
            await client.SubscribeToStreamAsync(
                streamName,
                FromStream.Start, // TODO: should be re-thinking what approach to use there
                async (_, resolvedEvent, ct) =>
                {
                    var eventPayloadAsBytes = resolvedEvent.Event.Data.ToArray();
                    using var eventDataStream = new MemoryStream(eventPayloadAsBytes);

                    var deserializedEvent = await JsonSerializer.DeserializeAsync<T>(eventDataStream, cancellationToken: ct);

                    if (deserializedEvent != null)
                    {
                        await onEventAppeared(deserializedEvent);
                    }
                },
                cancellationToken: cancellationToken
            );
        }

        public async Task SendToDeadLetterQueueAsync(BaseEvent eventForSending, Exception exception)
        {
            eventForSending.Metadata.Add(EventMetadataKeys.ExceptionDetails, exception.Message);

            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(eventForSending);

            var eventData = new EventData(
                Uuid.NewUuid(),
                "Dead_letter_event_type",
                utf8Bytes.AsMemory());

            await client.AppendToStreamAsync(
                    "Dead_letter_stream",
                    StreamState.Any,
                    [eventData],
                    deadline: TimeSpan.FromSeconds(options.TimeoutInSeconds));
        }

        private async Task EnsureSubscriptionAsync(string streamName, CancellationToken token)
        {
            if (AlreadySubscribedStreams.ContainsKey(streamName))
            {
                return;
            }

            await _subscriptionLock.WaitAsync(token);
            try
            {
                if (AlreadySubscribedStreams.ContainsKey(streamName))
                {
                    return;
                }

                var eventsAsyncStream = client.ReadStreamAsync(
                    Direction.Forwards,
                    streamName,
                    StreamPosition.Start,
                    deadline: TimeSpan.FromSeconds(options.TimeoutInSeconds),
                    cancellationToken: token
                );

                if (await eventsAsyncStream.ReadState != ReadState.StreamNotFound)
                {
                    await SubscribeToStreamAsync(streamName, OnEventAppearedAsync, cancellationToken: token);
                    AlreadySubscribedStreams[streamName] = true;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"BaseEventStoreClient: {ex.Message}");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        protected virtual Task OnEventAppearedAsync(T eventData)
        {
            return Task.CompletedTask;
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

            _subscriptionLock.Dispose();

            _disposed = true;
        }
    }
}
