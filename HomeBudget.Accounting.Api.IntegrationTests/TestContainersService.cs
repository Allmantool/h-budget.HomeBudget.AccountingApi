using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Testcontainers.EventStoreDb;
using Testcontainers.Kafka;
using Testcontainers.MongoDb;

using HomeBudget.Accounting.Infrastructure;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    internal class TestContainersService(IConfiguration configuration) : IAsyncDisposable
    {
        private readonly SemaphoreGuard _semaphoreGuard = new(new SemaphoreSlim(1));
        private static bool IsStarted { get; set; }

        public static EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public static KafkaContainer KafkaContainer { get; private set; }
        public static MongoDbContainer MongoDbContainer { get; private set; }

        public async Task UpAndRunningContainersAsync()
        {
            using (_semaphoreGuard)
            {
                if (configuration == null)
                {
                    return;
                }

                if (IsStarted)
                {
                    return;
                }

                IsStarted = true;

                try
                {
                    EventSourceDbContainer = new EventStoreDbBuilder()
                        .WithImage("eventstore/eventstore:23.10.0-jammy")
                        .WithName($"{nameof(TestContainersService)}-event-store-db-container")
                        .WithHostname("test-eventsource-db-host")
                        .WithPortBinding(2117, 2117)
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

                    MongoDbContainer = new MongoDbBuilder()
                        .WithImage("mongo:7.0.5-rc0-jammy")
                        .WithName($"{nameof(TestContainersService)}-mongo-db-container")
                        .WithHostname("test-mongo-db-host")
                        .WithPortBinding(28117, 28117)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .Build();

                    if (EventSourceDbContainer == null)
                    {
                        return;
                    }

                    if (KafkaContainer == null)
                    {
                        return;
                    }

                    if (MongoDbContainer == null)
                    {
                        return;
                    }

                    await Task.WhenAll(
                        EventSourceDbContainer.StartAsync(),
                        KafkaContainer.StartAsync(),
                        MongoDbContainer.StartAsync());
                }
                catch (Exception ex)
                {
                    IsStarted = false;
                    Console.WriteLine(ex);
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await EventSourceDbContainer.StopAsync();
                await KafkaContainer.StopAsync();
                await MongoDbContainer.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));

            IsStarted = false;
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

            if (MongoDbContainer != null)
            {
                await MongoDbContainer.DisposeAsync();
            }

            _semaphoreGuard?.Dispose();
        }
    }
}
