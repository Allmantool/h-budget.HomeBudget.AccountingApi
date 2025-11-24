using System;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using DotNet.Testcontainers.Containers;
using EventStore.Client;
using MongoDB.Driver;
using Testcontainers.EventStoreDb;
using Testcontainers.Kafka;
using Testcontainers.MongoDb;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.Factories;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Core.Constants;
using HomeBudget.Test.Core.Factories;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    internal class TestContainersService() : IAsyncDisposable
    {
        private static bool IsStarted { get; set; }
        private static bool Inizialized { get; set; }

        private static readonly SemaphoreGuard _semaphoreGuard = new(new SemaphoreSlim(1));
        public static EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public static IContainer KafkaUIContainer { get; private set; }
        public static KafkaContainer KafkaContainer { get; private set; }
        public static MongoDbContainer MongoDbContainer { get; private set; }

        public static bool IsReadyForUse => IsStarted && Inizialized;

        public static async Task<bool> UpAndRunningContainersAsync()
        {
            using (_semaphoreGuard)
            {
                if (IsReadyForUse)
                {
                    return true;
                }

                IsStarted = true;

                try
                {
                    EventSourceDbContainer = EventStoreDbContainerFactory.Build();

                    var networkName = $"test-kafka-net-{Guid.NewGuid().ToString("N").Substring(0, 6)}";

                    var testKafkaNetwork = await DockerNetworkFactory.GetOrCreateDockerNetworkAsync(networkName);

                    KafkaContainer = KafkaContainerFactory.Build(testKafkaNetwork);

                    KafkaUIContainer = await KafkaUIContainerFactory.BuildAsync(testKafkaNetwork);

                    MongoDbContainer = MongoDbContainerFactory.Build();

                    if (await TryToStartContainerAsync())
                    {
                        Inizialized = true;
                    }

                    return IsReadyForUse;
                }
                catch (Exception ex)
                {
                    IsStarted = false;
                    Inizialized = false;
                    Console.WriteLine(ex);

                    throw;
                }
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
                    BootstrapServers = KafkaContainer.GetBootstrapAddress(),
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
                await EventSourceDbContainer?.StopAsync();
                await KafkaContainer?.StopAsync();
                await KafkaUIContainer?.StopAsync();
                await MongoDbContainer?.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
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

            if (KafkaUIContainer != null)
            {
                await KafkaUIContainer.DisposeAsync();
            }

            if (MongoDbContainer != null)
            {
                await MongoDbContainer.DisposeAsync();
            }

            _semaphoreGuard?.Dispose();
        }

        private static async Task<bool> TryToStartContainerAsync()
        {
            try
            {
                if (EventSourceDbContainer is not null)
                {
                    await EventSourceDbContainer.SafeStartContainerAsync();
                }

                if (MongoDbContainer is not null)
                {
                    await MongoDbContainer.SafeStartContainerAsync();
                }

                if (KafkaUIContainer is not null)
                {
                    await KafkaUIContainer.SafeStartContainerAsync();
                }

                if (KafkaContainer is not null)
                {
                    await KafkaContainer.SafeStartContainerAsync(swallowBusyError: true);
                    await KafkaContainer.WaitForKafkaReadyAsync(TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes));
                }

                Console.WriteLine($"The topics have been created: {BaseTopics.AccountingAccounts}, {BaseTopics.AccountingPayments}");
            }
            catch (Exception ex)
            {
                IsStarted = false;
                Inizialized = false;

                Console.WriteLine("Container startup failed:");
                Console.WriteLine(ex);

                await MongoDbContainer.DumpContainerLogsSafelyAsync("MongoDB");
                await EventSourceDbContainer.DumpContainerLogsSafelyAsync("EventStoreDB");
                await KafkaContainer.DumpContainerLogsSafelyAsync("Kafka");

                throw;
            }

            return true;
        }
    }
}
