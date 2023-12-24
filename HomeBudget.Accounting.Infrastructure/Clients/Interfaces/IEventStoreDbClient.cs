using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IEventStoreDbClient<T>
    {
        Task<IWriteResult> SendAsync(T payload, string streamName = default, string eventType = default, CancellationToken token = default);

        IAsyncEnumerable<T> ReadAsync(string streamName, CancellationToken token = default);
    }
}
