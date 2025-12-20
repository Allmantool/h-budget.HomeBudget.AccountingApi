using System;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public abstract class PersistentSubscriptionConsumer
    {
        private readonly EventStorePersistentSubscriptionsClient _client;
        private readonly ILogger _logger;

        protected PersistentSubscriptionConsumer(
            EventStorePersistentSubscriptionsClient client,
            ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        protected async Task SubscribeAsync(
            string stream,
            string group,
            CancellationToken ct)
        {
            await _client.SubscribeToStreamAsync(
                stream,
                group,
                async (sub, ev, retryCount, token) =>
                {
                    try
                    {
                        await HandleEventAsync(ev, token);
                        await sub.Ack(ev);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error processing event {EventId} from {Stream}",
                            ev.Event.EventId,
                            ev.OriginalStreamId);

                        await sub.Nack(
                            PersistentSubscriptionNakEventAction.Retry,
                            ex.Message,
                            ev);
                    }
                },
                cancellationToken: ct);
        }

        protected abstract Task HandleEventAsync(
            ResolvedEvent resolvedEvent,
            CancellationToken ct);
    }
}
