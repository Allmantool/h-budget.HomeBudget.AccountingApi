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
    internal sealed class TestContainersService : IAsyncDisposable
    {
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static TestContainersService _instance;
        private bool _isDisposed;

        public bool IsReadyForUse { get; private set; }

        public EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public IContainer KafkaUIContainer { get; private set; }
        public KafkaContainer KafkaContainer { get; private set; }
        public MongoDbContainer MongoDbContainer { get; private set; }

        protected TestContainersService()
        {
        }

        public static async Task<TestContainersService> InitAsync()
        {
            if (_instance is not null)
            {
                return _instance;
            }

            await using (await SemaphoreGuard.WaitAsync(_lock))
            {
                if (_instance is null)
                {
                    _instance = new TestContainersService();
                    await _instance.UpAndRunningContainersAsync();
                }

                return _instance;
            }
        }

        public async Task ResetContainersAsync()
        {
            await using (await SemaphoreGuard.WaitAsync(_lock))
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
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

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

            _isDisposed = true;
        }

        private async Task<bool> UpAndRunningContainersAsync()
        {
            if (IsReadyForUse)
            {
                return true;
            }

            try
            {
                EventSourceDbContainer = EventStoreDbContainerFactory.Build();

                var networkName = $"test-kafka-net-{Guid.NewGuid().ToString("N").Substring(0, 6)}";

                var testKafkaNetwork = await DockerNetworkFactory.GetOrCreateDockerNetworkAsync(networkName);

                KafkaContainer = KafkaContainerFactory.Build(testKafkaNetwork);

                KafkaUIContainer = await KafkaUIContainerFactory.BuildAsync(testKafkaNetwork);

                MongoDbContainer = MongoDbContainerFactory.Build();

                await TryToStartContainerAsync();

                IsReadyForUse = true;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                IsReadyForUse = false;

                throw;
            }
        }

        private async Task<bool> TryToStartContainerAsync()
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
