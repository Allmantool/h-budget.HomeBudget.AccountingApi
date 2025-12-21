using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IEventStoreDbReadClient<T>
    {
        IAsyncEnumerable<T> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task SubscribeToStreamAsync(
            string streamName,
            Func<T, Task> onEventAppeared,
            CancellationToken cancellationToken = default);
    }
}
