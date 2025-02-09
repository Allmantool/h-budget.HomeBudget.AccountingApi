using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

using HomeBudget.Core;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IEventStoreDbClient<T>
    {
        Task<IWriteResult> SendAsync(
            T eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default);

        IAsyncEnumerable<T> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task SubscribeToStreamAsync(
            string streamName,
            Func<T, Task> onEventAppeared,
            CancellationToken cancellationToken = default);

        Task SendToDeadLetterQueueAsync(BaseEvent eventForSending, Exception exception);
    }
}
