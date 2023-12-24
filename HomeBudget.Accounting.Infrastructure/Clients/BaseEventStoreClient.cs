using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class BaseEventStoreClient<T>(EventStoreClient client) : IEventStoreDbClient<T>
        where T : new()
    {
        public virtual async Task<IWriteResult> SendAsync(
            T payload,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

            var eventData = new EventData(
                Uuid.NewUuid(),
                eventType ?? $"{nameof(T)}_{utf8Bytes.Length}",
                utf8Bytes.AsMemory());

            var writeResult = await client
                .AppendToStreamAsync(
                    streamName ?? nameof(T),
                    StreamState.Any,
                    new[] { eventData },
                    cancellationToken: token);

            return writeResult;
        }

        public virtual async IAsyncEnumerable<T> ReadAsync(string streamName, [EnumeratorCancellation] CancellationToken token = default)
        {
            var eventsAsyncStream = client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.Start,
                cancellationToken: token
            );

            if ((await eventsAsyncStream.ReadState) == ReadState.StreamNotFound)
            {
                yield return default;
            }

            await foreach (var paymentOperationEvent in eventsAsyncStream)
            {
                var eventPayloadAsBytes = paymentOperationEvent.Event.Data.ToArray();
                using var eventDataStream = new MemoryStream(eventPayloadAsBytes);

                var deserializationResult = JsonSerializer.DeserializeAsync<T>(eventDataStream, cancellationToken: token);

                if (deserializationResult.IsCompletedSuccessfully)
                {
                    yield return deserializationResult.Result;
                }
            }
        }
    }
}
