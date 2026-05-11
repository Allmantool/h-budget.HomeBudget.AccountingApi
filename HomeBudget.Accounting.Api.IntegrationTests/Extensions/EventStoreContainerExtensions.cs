using System;
using System.Threading.Tasks;

using EventStore.Client;
using Grpc.Core;
using Testcontainers.EventStoreDb;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class EventStoreContainerExtensions
    {
        private const string PaymentHistoryProjectionGroup = "ps-homeledger-mongo-projection-v1";

        public static async Task ResetContainersAsync(this EventStoreDbContainer container)
        {
            var esSettings = EventStoreClientSettings.Create(container.GetConnectionString());
            using var es = new EventStoreClient(esSettings);
            var persistentSubscriptions = new EventStorePersistentSubscriptionsClient(esSettings);

            await DeletePaymentHistoryProjectionSubscriptionAsync(persistentSubscriptions);

            var allEvents = es.ReadAllAsync(Direction.Forwards, Position.Start);

            await foreach (var record in allEvents)
            {
                var stream = record.Event.EventStreamId;

                if (stream.StartsWith('$'))
                {
                    continue;
                }

                try
                {
                    await es.SetStreamMetadataAsync(
                        stream,
                        StreamState.Any,
                        new StreamMetadata(truncateBefore: record.Event.EventNumber + 1)
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EventStore cleanup failed for stream {stream}: {ex}");
                }
            }
        }

        private static async Task DeletePaymentHistoryProjectionSubscriptionAsync(
            EventStorePersistentSubscriptionsClient persistentSubscriptions)
        {
            try
            {
                await persistentSubscriptions.DeleteToAllAsync(PaymentHistoryProjectionGroup);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
            }
            catch (PersistentSubscriptionNotFoundException)
            {
            }
        }
    }
}
