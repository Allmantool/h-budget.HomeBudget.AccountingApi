using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class EventStoreWriter<T>
        where T : class
    {
        private readonly EventStoreClient _client;

        protected EventStoreWriter(EventStoreClient client)
        {
            _client = client;
        }

        public async Task<IWriteResult> AppendAsync(
            string streamName,
            T @event,
            string eventType,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(@event);
            ArgumentException.ThrowIfNullOrWhiteSpace(streamName);

            var eventData = ToEventData(@event, eventType);

            return await _client.AppendToStreamAsync(
                streamName,
                StreamState.Any,
                [eventData],
                cancellationToken: ct);
        }

        protected static EventData ToEventData(T @event, string eventType)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(@event);

            return new EventData(
                Uuid.NewUuid(),
                eventType ?? typeof(T).Name,
                payload);
        }
    }
}
