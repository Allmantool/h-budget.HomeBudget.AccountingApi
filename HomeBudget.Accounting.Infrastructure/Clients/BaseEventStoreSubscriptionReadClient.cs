using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Logs;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public class BaseEventStoreSubscriptionReadClient<T> : IEventStoreDbSubscriptionReadClient<T>, IDisposable
        where T : class, new()
    {
        private readonly ILogger _logger;
        private readonly EventStorePersistentSubscriptionsClient _client;
        private readonly EventStoreDbOptions _options;

        private bool _disposed;
        private PersistentSubscription _subscription;

        protected BaseEventStoreSubscriptionReadClient(
            EventStorePersistentSubscriptionsClient client,
            EventStoreDbOptions options,
            ILogger logger)
        {
            _client = client;
            _logger = logger;
            _options = options;
        }

        public virtual Task CreatePersistentSubscriptionAsync(CancellationToken ct) => Task.CompletedTask;

        public virtual async Task CreatePersistentSubscriptionAsync(string streamName, string groupName, CancellationToken ct)
        {
            try
            {
                var settings = new PersistentSubscriptionSettings(
                    resolveLinkTos: true,
                    startFrom: StreamPosition.Start);

                await _client.CreateToStreamAsync(
                    streamName,
                    groupName,
                    settings,
                    cancellationToken: ct);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                // expected
            }
            catch (Exception ex)
            {
                // expected
            }
        }

        public virtual Task SubscribeAsync(CancellationToken ct = default) => SubscribeAsync(null, ct);
        public virtual Task SubscribeAsync(Func<ResolvedEvent, Task> handler = null, CancellationToken ct = default) => Task.CompletedTask;

        public virtual async Task SubscribeAsync(
            string streamName,
            string groupName,
            Func<ResolvedEvent, Task> handler = null,
            CancellationToken ct = default)
        {
            try
            {
                _subscription = await _client.SubscribeToStreamAsync(
                      streamName,
                      groupName,
                      async (sub, evt, retryCount, token) =>
                      {
                          if (handler is null)
                          {
                              var json = evt.Event.Data.Span;
                              var eventType = evt.Event.EventType;

                              var eventData = JsonSerializer.Deserialize<T>(json);

                              await OnEventAppearedAsync(eventData);
                          }
                          else
                          {
                              await handler(evt);
                          }

                          await sub.Ack(evt);
                      },
                      (sub, reason, ex) =>
                      {
                          _logger.SubscriptionDropped(ex, reason);
                      },
                      cancellationToken: ct);

                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.SubscriptionFailed(ex);
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryInSeconds), ct);
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
