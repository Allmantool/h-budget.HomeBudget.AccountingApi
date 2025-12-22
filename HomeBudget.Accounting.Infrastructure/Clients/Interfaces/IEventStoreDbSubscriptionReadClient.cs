using System;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IEventStoreDbSubscriptionReadClient<T>
    {
        Task CreatePersistentSubscriptionAsync(CancellationToken ct);

        Task CreatePersistentSubscriptionAsync(string groupName, CancellationToken ct);

        Task<PersistentSubscription> SubscribeAsync(
            string groupName,
            Func<ResolvedEvent, Task> handler,
            CancellationToken ct);

        Task<PersistentSubscription> SubscribeAsync(CancellationToken ct);
    }
}
