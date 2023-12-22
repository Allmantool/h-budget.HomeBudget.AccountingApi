using System.Threading.Tasks;

using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public interface IEventStoreDbClient
    {
        Task<IWriteResult> SendAsync<T>(T payload, string eventType);
    }
}
