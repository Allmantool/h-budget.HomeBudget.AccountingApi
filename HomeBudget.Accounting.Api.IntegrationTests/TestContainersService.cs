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
        public static bool IsStarted { get; private set; }

        private static readonly SemaphoreGuard _semaphoreGuard = new(new SemaphoreSlim(1));
        public static EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public static IContainer KafkaUIContainer { get; private set; }
        public static KafkaContainer KafkaContainer { get; private set; }
        public static MongoDbContainer MongoDbContainer { get; private set; }

        public static async Task<bool> UpAndRunningContainersAsync()
        {
            using (_semaphoreGuard)
            {
                if (IsStarted)
                {
                    return IsStarted;
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

                    await TryToStartContainerAsync();
                    await KafkaContainer.WaitForKafkaReadyAsync(TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes));

                    Console.WriteLine($"The topics have been created: {BaseTopics.AccountingAccounts}, {BaseTopics.AccountingPayments}");

                    return IsStarted;
                }
                catch (Exception ex)
                {
                    IsStarted = false;
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
                    await EventSourceDbContainer.StartAsync();
                }

                if (KafkaContainer is not null)
                {
                    await KafkaContainer.StartAsync();
                }

                if (KafkaUIContainer is not null)
                {
                    await KafkaUIContainer.StartAsync();
                }

                if (MongoDbContainer is not null)
                {
                    await MongoDbContainer.StartAsync();
                }
            }
            catch (Exception ex)
            {
                // Ignore "device or resource busy" as a false positive
                if (ex.Message.Contains("testcontainers.sh: device or resource busy", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                IsStarted = false;
                Console.WriteLine(ex);

                var mongoDbLogs = await MongoDbContainer.GetLogsAsync();
                Console.WriteLine($"Mongo db container logs: {mongoDbLogs}");

                var eventSourceDbLogs = await EventSourceDbContainer.GetLogsAsync();
                Console.WriteLine($"Event store db container logs: {eventSourceDbLogs}");

                var kafkaLobs = await KafkaContainer.GetLogsAsync();
                Console.WriteLine($"Kafka container logs: {kafkaLobs}");

                throw;
            }

            return true;
        }
    }
}
