using System;
using System.Text.Json;
using System.Threading.Tasks;

using EventStore.Client;

using HomeBudget.Accounting.Infrastructure.Clients;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient(EventStoreClient client) : IEventStoreDbClient
    {
        private const string PaymentsStreamName = "PaymentsStream";

        public async Task<IWriteResult> SendAsync<T>(T payload, string eventType)
        {
            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

            var eventData = new EventData(
                Uuid.NewUuid(),
                eventType,
                utf8Bytes.AsMemory());

            var writeResult = await client
                .AppendToStreamAsync(
                    PaymentsStreamName,
                    StreamState.Any,
                    new[] { eventData });

            return writeResult;
        }
    }
}
