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

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Logs;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    [Obsolete("Not to go along with prod grade approach. Will be re-written accodring to SOLID, etc")]
    public abstract class BaseEventStoreStreamReadClient<T> : IEventStoreDbStreamReadClient<T>, IDisposable
        where T : class, new()
    {
        private readonly EventStoreDbOptions _options;
        private readonly EventStoreClient _client;
        private readonly ILogger _logger;
        private bool _disposed;
        private static readonly ConcurrentDictionary<string, bool> SubscribedStreams = new(); // should be out of proccess cache (re-use rates implementation)

        protected BaseEventStoreStreamReadClient(
            EventStoreClient client,
            EventStoreDbOptions options,
            ILogger logger)
        {
            _client = client;
            _logger = logger;
            _options = options;
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
            try
            {
                await _client.SubscribeToStreamAsync(
                    streamName,
                    FromStream.End,
                    async (_, resolvedEvent, ct) =>
                    {
                        var evt = JsonSerializer.Deserialize<T>(resolvedEvent.Event.Data.Span);
                        if (evt != null)
                        {
                            await onEventAppeared(evt);
                        }
                    },
                    cancellationToken: cancellationToken);

                SubscribedStreams.TryAdd(streamName, true);
            }
            catch (Exception ex)
            {
                _logger.SubscriptionError(streamName, ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

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
