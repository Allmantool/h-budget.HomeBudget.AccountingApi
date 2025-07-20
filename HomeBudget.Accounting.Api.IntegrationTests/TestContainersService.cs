using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Testcontainers.EventStoreDb;
using Testcontainers.Kafka;
using Testcontainers.MongoDb;

using HomeBudget.Accounting.Api.IntegrationTests.Factories;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Core.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    internal class TestContainersService(IConfiguration configuration) : IAsyncDisposable
    {
        private readonly SemaphoreGuard _semaphoreGuard = new(new SemaphoreSlim(1));
        private static bool IsStarted { get; set; }

        public static EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public static IContainer KafkaUIContainer { get; private set; }
        public static IContainer ZookeperKafkaContainer { get; private set; }
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
                        .WithImage("eventstore/eventstore:24.10.4-jammy")
                        .WithName($"{nameof(TestContainersService)}-event-store-db-container")
                        .WithHostname("test-eventsource-db-host")
                        .WithPortBinding(2117, 2117)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig.Memory = ContainerMaxMemoryAllocation;
                        })
                        .Build();

                    var testcontainersScriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Scripts/testcontainers.sh"))
                        .Replace('\\', '/');

                    if (!File.Exists(testcontainersScriptPath))
                    {
                        throw new FileNotFoundException($"Missing script: {testcontainersScriptPath}");
                    }

                    var testKafkaNetwork = await DockerNetworkFactory.GetOrCreateDockerNetworkAsync("test-kafka-net");

                    KafkaUIContainer = await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                        $"{nameof(TestContainersService)}-kafka-test-ui",
                        cb => cb.WithImage("provectuslabs/kafka-ui:v0.7.2")
                        .WithName($"{nameof(TestContainersService)}-kafka-test-ui")
                        .WithHostname("test-kafka-ui")
                        .WithPortBinding(8080, 8080)
                        .WithEnvironment("DYNAMIC_CONFIG_ENABLED", "true")
                        .WithEnvironment("KAFKA_CLUSTERS_0_NAME", "test-cluster-kafka")
                        .WithEnvironment("KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS", "test-kafka:9093")
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithNetwork(testKafkaNetwork)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .Build());

                    ZookeperKafkaContainer = await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                        $"{nameof(TestContainersService)}-test-zookeper",
                        cb => cb
                        .WithImage("confluentinc/cp-zookeeper:7.9.0")
                        .WithName($"{nameof(TestContainersService)}-test-zookeper")
                        .WithHostname("test-zookeper")
                        .WithEnvironment("ZOOKEEPER_CLIENT_PORT", "2181")
                        .WithEnvironment("ZOOKEEPER_TICK_TIME", "2000")
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithNetwork(testKafkaNetwork)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .Build());

                    KafkaContainer = new KafkaBuilder()
                        .WithImage("confluentinc/cp-kafka:7.9.0")
                        .WithName($"{nameof(TestContainersService)}-kafka-container")
                        .WithHostname("test-kafka")
                        .WithPortBinding(9092, 9092)
                        .WithPortBinding(29092, 29092)

                         // v8+ KRAFT_MODE
                         // .WithEnvironment("KAFKA_KRAFT_MODE", "true")
                         // .WithEnvironment("CLUSTER_ID", "5Y7pZQq4Td6Jv4n3z2Z8Zg")
                         // .WithEnvironment("KAFKA_NODE_ID", "1")
                         // .WithEnvironment("KAFKA_LOG_DIRS", "/tmp/kraft-combined-logs")
                         // .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
                         // .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@kafka:29093")
                         // .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
                         // .WithStartupCallback((kafkaContainer, cancelToken) =>
                         // {
                         //    return Task.CompletedTask;
                         // })
                         // .WithBindMount(testcontainersScriptPath, "/testcontainers.sh", AccessMode.ReadOnly)
                         // .WithBindMount(serverProperties, "/etc/kafka/server.properties", AccessMode.ReadOnly)
                         // .WithEnvironment("KAFKA_JMX_PORT", "9997")
                         // .WithEnvironment("KAFKA_JMX_HOSTNAME", "kafka")

                         // Base kafka options
                         .WithEnvironment("KAFKA_BROKER_ID", "1")
                         .WithEnvironment(
                            "KAFKA_ZOOKEEPER_CONNECT",
                            $"test-zookeper:2181")
                         .WithEnvironment(
                           "KAFKA_LISTENERS",
                           "PLAINTEXT://0.0.0.0:9092,BROKER://0.0.0.0:9093")
                         .WithEnvironment(
                           "KAFKA_ADVERTISED_LISTENERS",
                           "PLAINTEXT://localhost:9092,BROKER://test-kafka:9093")
                         .WithEnvironment(
                            "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                            "PLAINTEXT:PLAINTEXT,BROKER:PLAINTEXT")
                        .WithEnvironment(
                            "KAFKA_INTER_BROKER_LISTENER_NAME",
                            "BROKER")
                        .WithEnvironment("KAFKA_LOG_RETENTION_BYTES", "1073741824")
                        .WithEnvironment("KAFKA_LOG_CLEANUP_POLICY", "delete")
                        .WithEnvironment("KAFKA_DELETE_TOPIC_ENABLE", "true")
                        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true")
                        .WithEnvironment("KAFKA_OFFSETS_TOPIC_NUM_PARTITIONS", "30")
                        .WithEnvironment("KAFKA_LOG_RETENTION_HOURS", "168")
                        .WithEnvironment("KAFKA_LOG_SEGMENT_BYTES", "1073741824")
                        .WithEnvironment("KAFKA_LOG_RETENTION_CHECK_INTERVAL_MS", "300000")
                        .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
                        .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
                        .WithNetwork(testKafkaNetwork)
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig.Memory = ContainerMaxMemoryAllocation;
                            config.HostConfig.NanoCPUs = 1500000000;
                        })

                        // .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .Build();

                    MongoDbContainer = new MongoDbBuilder()
                        .WithImage("mongo:7.0.5-rc0-jammy")
                        .WithName($"{nameof(TestContainersService)}-mongo-db-container")
                        .WithHostname("test-mongo-db-host")
                        .WithPortBinding(28117, 28117)
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig.Memory = ContainerMaxMemoryAllocation;
                        })
                        .Build();

                    await Task.WhenAll(
                        EventSourceDbContainer.StartAsync(),
                        ZookeperKafkaContainer.StartAsync(),
                        KafkaContainer.StartAsync(),
                        KafkaUIContainer.StartAsync(),
                        MongoDbContainer.StartAsync());

                    await Task.Delay(TimeSpan.FromSeconds(60));

                    var config = new AdminClientConfig
                    {
                        BootstrapServers = KafkaContainer.GetBootstrapAddress()
                    };

                    using var admin = new AdminClientBuilder(config).Build();

                    await admin.CreateTopicsAsync(
                    [
                        new TopicSpecification { Name = BaseTopics.AccountingAccounts, NumPartitions = 1, ReplicationFactor = 1 },
                        new TopicSpecification { Name = BaseTopics.AccountingPayments, NumPartitions = 1, ReplicationFactor = 1 }
                    ]);
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
                await ZookeperKafkaContainer?.StopAsync();
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

            if (ZookeperKafkaContainer != null)
            {
                await ZookeperKafkaContainer.DisposeAsync();
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
    }
}
