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

                _logger.LogInformation(
                    "Persistent subscription '{Group}' created on $all",
                    groupName);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                _logger.LogInformation(
                    "Persistent subscription '{Group}' already exists",
                    groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Failed to create subscription with error",
                    groupName);
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
                        if (evt.Event is null)
                        {
                            return;
                        }

                        if (evt.Event.EventType.StartsWith("$"))
                        {
                            await sub.Ack(evt);
                            return;
                        }

                        if (!evt.Event.EventStreamId.StartsWith("payment-account-"))
                        {
                            await sub.Ack(evt);
                            return;
                        }

                        try
                        {
                            var eventData = JsonSerializer.Deserialize<T>(evt.Event.Data.Span);

                            if (eventData is null)
                            {
                                await sub.Nack(
                                    PersistentSubscriptionNakEventAction.Skip,
                                    "Deserialization failed",
                                    evt);

                                return;
                            }

                            if (handler is null)
                            {
                                await OnEventAppearedAsync(eventData);
                            }
                            else
                            {
                                await handler(evt);
                            }

                            await sub.Ack(evt);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Handler failed for event {EventId}",
                                evt.Event.EventId);

                            await sub.Nack(
                                PersistentSubscriptionNakEventAction.Retry,
                                ex.Message,
                                evt);
                        }
                    },
                    (sub, reason, ex) =>
                    {
                        _logger.LogWarning(
                            ex,
                            "Subscription dropped: {Reason}",
                            reason);

                        droppedTcs.TrySetResult();
                    },
                    cancellationToken: ct);

                using (ct.Register(() => droppedTcs.TrySetCanceled(ct)))
                {
                    await droppedTcs.Task;
                }

                return subscription;
            }
            catch
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
