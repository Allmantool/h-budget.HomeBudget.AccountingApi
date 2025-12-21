using System;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Clients
{
    internal sealed class PaymentOperationsEventStoreSubscriptionReadClient
        : BaseEventStoreSubscriptionReadClient<PaymentOperationEvent>
    {
        private const string Stream = "$ce-payment-account";
        private const string Group = "ps-homeledger-mongo-projection-v1";

        public PaymentOperationsEventStoreSubscriptionReadClient(
            ILogger<PaymentOperationsEventStoreSubscriptionReadClient> logger,
            EventStorePersistentSubscriptionsClient client,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
        }

        public override Task CreatePersistentSubscriptionAsync(CancellationToken ct)
        {
            return CreatePersistentSubscriptionAsync(Stream, Group, ct);
        }

        public override Task SubscribeAsync(Func<ResolvedEvent, Task> handler = null, CancellationToken ct = default)
        {
            return SubscribeAsync(Stream, Group, handler, ct);
        }
    }
}
