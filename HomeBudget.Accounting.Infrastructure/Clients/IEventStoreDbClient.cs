using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public interface IEventStoreDbClient
    {
        Task<IWriteResult> SendAsync<T>(T payload, string eventType, CancellationToken token = default);

        IAsyncEnumerable<T> ReadAsync<T>(CancellationToken token = default);
    }
}
