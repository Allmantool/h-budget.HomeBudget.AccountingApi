using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Docker.DotNet.Models;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
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
                if (configuration == null || IsStarted)
                {
                    return;
                }

                IsStarted = true;

                const long ContainerMaxMemoryAllocation = 1024 * 1024 * 1024;
                try
                {
                    EventSourceDbContainer = new EventStoreDbBuilder()
                        .WithImage("eventstore/eventstore:23.10.0-jammy")
                        .WithName($"{nameof(TestContainersService)}-event-store-db-container")
                        .WithHostname("test-eventsource-db-host")
                        .WithPortBinding(2117, 2117)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig = new HostConfig
                            {
                                Memory = ContainerMaxMemoryAllocation,
                            };
                        })
                        .Build();

                    KafkaContainer = new KafkaBuilder()
                        .WithImage("confluentinc/cp-kafka:7.4.3")
                        .WithName($"{nameof(TestContainersService)}-kafka-container")
                        .WithHostname("test-kafka-host")
                        .WithPortBinding(9092, 9092)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig = new HostConfig
                            {
                                Memory = ContainerMaxMemoryAllocation,
                            };
                        })
                        .Build();

                    MongoDbContainer = new MongoDbBuilder()
                        .WithImage("mongo:7.0.5-rc0-jammy")
                        .WithName($"{nameof(TestContainersService)}-mongo-db-container")
                        .WithHostname("test-mongo-db-host")
                        .WithPortBinding(28117, 28117)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig = new HostConfig
                            {
                                Memory = ContainerMaxMemoryAllocation,
                            };
                        })
                        .Build();

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

        public static async Task ResetContainersAsync()
        {
            try
            {
                using var mongoClient = new MongoClient(MongoDbContainer.GetConnectionString());
                var databases = await mongoClient.ListDatabaseNamesAsync();
                foreach (var dbName in databases.ToList())
                {
                    if (dbName != "admin" && dbName != "local" && dbName != "config")
                    {
                        await mongoClient.DropDatabaseAsync(dbName);
                    }
                }

                var config = new AdminClientConfig
                {
                    BootstrapServers = KafkaContainer.GetBootstrapAddress()
                };
                using var adminClient = new AdminClientBuilder(config).Build();
                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
                foreach (var topic in metadata.Topics)
                {
                    if (!topic.Topic.StartsWith('_'))
                    {
                        await adminClient.DeleteTopicsAsync([topic.Topic]);
                    }
                }

                var settings = EventStoreClientSettings.Create(EventSourceDbContainer.GetConnectionString());
                using var eventStoreClient = new EventStoreClient(settings);
                await foreach (var stream in eventStoreClient.ReadAllAsync(Direction.Forwards, Position.Start))
                {
                    var streamId = stream.Event.EventStreamId;

                    if (!streamId.StartsWith('$'))
                    {
                        try
                        {
                            await eventStoreClient.TombstoneAsync(streamId, StreamState.Any);
                        }
                        catch (StreamDeletedException)
                        {
                            Console.WriteLine($"Stream {streamId} is already deleted.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        public static async Task StopAsync()
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
