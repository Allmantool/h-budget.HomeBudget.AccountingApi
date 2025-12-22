using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    [Obsolete("Not to go along with prod grade approach. Will be re-written accodring to SOLID, etc")]
    public abstract class BaseEventStoreWriteClient<T> : IEventStoreDbWriteClient<T>, IDisposable
        where T : class, new()
    {
        private readonly EventStoreClient _client;
        private readonly ILogger _logger;
        private readonly EventStoreDbOptions _options;

        private bool _disposed;

        protected BaseEventStoreWriteClient(EventStoreClient client, EventStoreDbOptions options, ILogger logger)
        {
            _client = client;
            _logger = logger;
            _options = options;
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

            return await _client.AppendToStreamAsync(
                writeStreamName,
                StreamState.Any,
                [eventData],
                cancellationToken: token);
        }

        public virtual async Task<IWriteResult> SendBatchAsync(
            IEnumerable<T> eventsForSending,
            string streamName,
            string eventType = null,
            CancellationToken ctx = default)
        {
            ArgumentNullException.ThrowIfNull(eventsForSending);

            var eventsToSend = eventsForSending
                .AsParallel()
                .Select(e => CreateEventData(e, eventType))
                .ToList();

            if (eventsToSend.Count == 0)
            {
                throw new ArgumentException("Batch is empty", nameof(eventsForSending));
            }

            var writeStreamName = streamName ?? typeof(T).Name;

            var appendResult = await _client.AppendToStreamAsync(
                writeStreamName,
                StreamState.Any,
                eventsToSend,
                cancellationToken: ctx);

            return appendResult;
        }

        public async Task SendToDeadLetterQueueAsync(BaseEvent eventForSending, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            eventForSending.Metadata ??= [];
            eventForSending.Metadata[EventMetadataKeys.ExceptionDetails] = exception.Message;

            var data = CreateEventData((T)(object)eventForSending, EventDbEventTypes.DeadLetter);

            await _client.AppendToStreamAsync(
                EventDbEventStreams.DeadLetter,
                StreamState.Any,
                [data],
                cancellationToken: CancellationToken.None);
        }

        public async Task SendToDeadLetterQueueAsync(
            IEnumerable<BaseEvent> eventsForSending,
            Exception exception)
        {
            Parallel.ForEach(eventsForSending, eventForSending =>
            {
                eventForSending.Metadata ??= new Dictionary<string, string>();
                eventForSending.Metadata[EventMetadataKeys.ExceptionDetails] = exception?.Message;
            });

            var data = eventsForSending
                .AsParallel()
                .Select(ev => CreateEventData((T)(object)ev, EventDbEventTypes.DeadLetter));

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutInSeconds));
            await _client.AppendToStreamAsync(
                EventDbEventStreams.DeadLetter,
                StreamState.Any,
                data,
                cancellationToken: cts.Token);
        }

        private static EventData CreateEventData(T @event, string eventType)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(@event);
            return new EventData(Uuid.NewUuid(), eventType ?? typeof(T).Name, bytes.AsMemory());
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
