using System.Collections.Generic;
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
        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent payload,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            var paymentAccountId = payload.Payload.PaymentAccountId;
            var paymentOperationId = payload.Payload.Key;

            return await base.SendAsync(
                payload,
                PaymentOperationNamesGenerator.GetEventSteamName(paymentAccountId.ToString()),
                $"{payload.EventType}_{paymentOperationId}",
                token);
        }

        public override IAsyncEnumerable<PaymentOperationEvent> ReadAsync(string streamName, CancellationToken token = default)
        {
            return base.ReadAsync(PaymentOperationNamesGenerator.GetEventSteamName(streamName), token);
        }
    }
}
