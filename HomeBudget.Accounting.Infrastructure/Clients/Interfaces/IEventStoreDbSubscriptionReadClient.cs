using System;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IEventStoreDbSubscriptionReadClient<T>
    {
        Task CreatePersistentSubscriptionAsync(string streamName, string groupName, CancellationToken ct);

        Task SubscribeAsync(
            string streamName,
            string groupName,
            Func<ResolvedEvent, Task> handler,
            CancellationToken ct);
    }
}
