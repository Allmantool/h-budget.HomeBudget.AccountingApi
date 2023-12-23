using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;

using HomeBudget.Accounting.Infrastructure.Clients;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient(EventStoreClient client)
        : IEventStoreDbClient
    {
        private const string PaymentsStreamName = "PaymentsStream";

        public async Task<IWriteResult> SendAsync<T>(T payload, string eventType, CancellationToken token = default)
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
                    new[] { eventData },
                    cancellationToken: token);

            return writeResult;
        }

        public async IAsyncEnumerable<T> ReadAsync<T>([EnumeratorCancellation] CancellationToken token = default)
        {
            var eventsAsyncStream = client.ReadStreamAsync(
                Direction.Forwards,
                PaymentsStreamName,
                StreamPosition.Start,
                cancellationToken: token
            );

            await foreach (var paymentOperationEvent in eventsAsyncStream)
            {
                var eventPayloadAsBytes = paymentOperationEvent.Event.Data.ToArray();
                using var eventDataStream = new MemoryStream(eventPayloadAsBytes);

                var deserializationResult = JsonSerializer.DeserializeAsync<T>(eventDataStream, cancellationToken: token);

                if (deserializationResult.IsCompletedSuccessfully)
                {
                    yield return deserializationResult.Result;
                }
            }
        }
    }
}
