using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient(EventStoreClient client)
        : BaseEventStoreClient<PaymentOperationEvent>(client)
    {
        public async Task<IWriteResult> SendAsync(PaymentOperationEvent payload, CancellationToken token = default)
        {
            var paymentAccountId = payload.Payload.PaymentAccountId;
            var paymentOperationId = payload.Payload.Key;

            var eventType = $"{payload.EventType}_{paymentOperationId}";

            return await SendAsync(
                payload,
                PaymentOperationNamesGenerator.GetEventSteamName(paymentAccountId.ToString()),
                eventType,
                token);
        }
    }
}
