using System;
using System.Threading.Tasks;

using EventStore.Client;
using Testcontainers.EventStoreDb;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class EventStoreContainerExtensions
    {
        public static async Task ResetContainersAsync(this EventStoreDbContainer container)
        {
            var esSettings = EventStoreClientSettings.Create(container.GetConnectionString());
            using var es = new EventStoreClient(esSettings);

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
    }
}
