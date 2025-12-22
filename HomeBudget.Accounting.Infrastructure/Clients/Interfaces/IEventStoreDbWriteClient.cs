using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

using HomeBudget.Core;

namespace HomeBudget.Accounting.Infrastructure.Clients.Interfaces
{
    public interface IEventStoreDbWriteClient<T>
    {
        Task<IWriteResult> SendAsync(
            T eventForSending,
            string streamName,
            string eventType,
            CancellationToken token = default);

        Task<IWriteResult> SendBatchAsync(
            IEnumerable<T> eventsForSending,
            string streamName,
            string eventType = null,
            CancellationToken ctx = default);

        Task SendToDeadLetterQueueAsync(BaseEvent eventForSending, Exception exception);
    }
}
