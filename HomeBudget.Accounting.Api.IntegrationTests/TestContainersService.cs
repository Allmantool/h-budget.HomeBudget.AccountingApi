using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Testcontainers.EventStoreDb;
using Testcontainers.Kafka;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    internal class TestContainersService(IConfiguration configuration) : IAsyncDisposable
    {
        public EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public KafkaContainer KafkaContainer { get; private set; }

        public async Task UpAndRunningContainersAsync()
        {
            if (configuration == null)
            {
                return;
            }

            EventSourceDbContainer = new EventStoreDbBuilder()
                .WithImage("eventstore/eventstore:23.10.0-jammy")
                .WithName($"{nameof(TestContainersService)}-event-store-db-container")
                .WithHostname("test-eventsource-db-host")
                .WithPortBinding(2113, 2113)
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .Build();

            KafkaContainer = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:7.4.3")
                .WithName($"{nameof(TestContainersService)}-kafka-container")
                .WithHostname("test-kafka-host")
                .WithPortBinding(9092, 9092)
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .Build();

            if (EventSourceDbContainer != null)
            {
                await EventSourceDbContainer.StartAsync();
            }

            if (KafkaContainer != null)
            {
                await KafkaContainer.StartAsync();
            }
        }

        public async Task StopAsync()
        {
            await EventSourceDbContainer.StopAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (EventSourceDbContainer != null)
            {
                await EventSourceDbContainer.DisposeAsync();
            }

            if (KafkaContainer != null)
            {
                await KafkaContainer.DisposeAsync();
            }
        }
    }
}
