using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
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
                        .WithName($"{nameof(TestContainersService)}-event-store-db-container-{Guid.NewGuid()}")
                        .WithHostname("test-eventsource-db-host")
                        .WithPortBinding(2117, 2117)
                        .WithEnvironment("EVENTSTORE_INSECURE", "true")
                        .WithEnvironment("EVENTSTORE_HTTP_PORT", "2117")
                        .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "true")
                        .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
                        .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "System")
                        .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
                        .WithEnvironment("EVENTSTORE_COMMIT_TIMEOUT_MS", "60000")
                        .WithEnvironment("EVENTSTORE_WRITE_TIMEOUT_MS", "60000")
                        .WithEnvironment("EVENTSTORE_DISABLE_HTTP_CACHING", "true")
                        .WithEnvironment("EVENTSTORE_MAX_MEM_TABLE_SIZE", "100000")
                        .WithEnvironment("EVENTSTORE_CACHED_CHUNKS", "512")
                        .WithEnvironment("EVENTSTORE_MIN_FLUSH_DELAY_MS", "2000")
                        .WithEnvironment("EVENTSTORE_STATS_PERIOD_SEC", "60")
                        .WithEnvironment("EVENTSTORE_SKIP_DB_VERIFY", "true")
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

                    KafkaContainer = new KafkaBuilder()
                        .WithImage("confluentinc/cp-kafka:8.1.0")
                        .WithName($"{nameof(TestContainersService)}-kafka-container-{Guid.NewGuid()}")
                        .WithHostname("test-kafka")
                        .WithPortBinding(9092, 9092)
                        .WithPortBinding(29093, 29093)

                        // v8+ KRAFT_MODE
                        .WithEnvironment("KAFKA_KRAFT_MODE", "true")
                        .WithEnvironment("KAFKA_NODE_ID", "1")
                        .WithEnvironment("CLUSTER_ID", "5Y7pZQq4Td6Jv4n3z2Z8Zg")
                        .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
                        .WithEnvironment("KAFKA_LISTENERS", "PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:29093")
                        .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "PLAINTEXT://test-kafka:9092")
                        .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT")
                        .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@test-kafka:29093")
                        .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
                        .WithEnvironment("KAFKA_JMX_PORT", "9997")
                        .WithEnvironment("KAFKA_JMX_HOSTNAME", "kafka")
                        .WithEnvironment("KAFKA_LOG_DIRS", "/tmp/kraft-combined-logs")
                        .WithBindMount(testcontainersScriptPath, "/testcontainers.sh", AccessMode.ReadOnly)
                        .WithStartupCallback((kafkaContainer, cancelToken) =>
                         {
                             return Task.CompletedTask;
                         })
                        .WithEnvironment("KAFKA_DELETE_TOPIC_ENABLE", "true")
                        .WithEnvironment("KAFKA_LOG_RETENTION_HOURS", "168")
                        .WithEnvironment("KAFKA_LOG_SEGMENT_BYTES", "1073741824")
                        .WithEnvironment("KAFKA_LOG_RETENTION_CHECK_INTERVAL_MS", "300000")
                        .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
                        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true")
                        .WithEnvironment("KAFKA_LOG_CLEANUP_POLICY", "delete")
                        .WithEnvironment("KAFKA_LOG_RETENTION_BYTES", "1073741824")
                        .WithEnvironment("KAFKA_OFFSETS_TOPIC_NUM_PARTITIONS", "30")
                        .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
                        .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
                        .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
                        .WithNetwork(testKafkaNetwork)
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig.Memory = ContainerMaxMemoryAllocation;
                            config.HostConfig.NanoCPUs = 1500000000;
                        })

                        // .WithAutoRemove(true)
                        // .WithCleanUp(true)
                        .Build();

                    KafkaUIContainer = await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                        $"{nameof(TestContainersService)}-kafka-test-ui",
                        cb => cb.WithImage("provectuslabs/kafka-ui:v0.7.2")
                        .WithName($"{nameof(TestContainersService)}-kafka-test-ui")
                        .WithHostname($"test-kafka-ui")
                        .WithPortBinding(8080, 8080)
                        .WithEnvironment("DYNAMIC_CONFIG_ENABLED", "true")
                        .WithEnvironment("KAFKA_CLUSTERS_0_NAME", $"test-kafka-cluster")
                        .WithEnvironment("KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS", "test-kafka:9092")
                        .WithWaitStrategy(Wait.ForUnixContainer())
                        .WithNetwork(testKafkaNetwork)

                        // .WithAutoRemove(true)
                        // .WithCleanUp(true)
                        .Build());

                    MongoDbContainer = new MongoDbBuilder()
                        .WithImage("mongo:7.0.5-rc0-jammy")
                        .WithName($"{nameof(TestContainersService)}-mongo-db-container-{Guid.NewGuid()}")
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

                    await TryToStartContainerAsync();

                    // Wait until Kafka is fully ready
                    var bootstrapServers = KafkaContainer.GetBootstrapAddress();
                    await WaitForKafkaReadyAsync(bootstrapServers, TimeSpan.FromSeconds(30));

                    var config = new AdminClientConfig
                    {
                        BootstrapServers = bootstrapServers
                    };

                    using var admin = new AdminClientBuilder(config).Build();

                    await admin.CreateTopicsAsync(
                    [
                        new TopicSpecification
                        {
                            Name = BaseTopics.AccountingAccounts,
                            NumPartitions = 1,
                            ReplicationFactor = 1
                        },
                        new TopicSpecification
                        {
                            Name = BaseTopics.AccountingPayments,
                            NumPartitions = 5,
                            ReplicationFactor = 1
                        },
                    ]);
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
                await Task.WhenAll(
                    EventSourceDbContainer.StartAsync(),
                    KafkaContainer.StartAsync(),
                    KafkaUIContainer.StartAsync(),
                    MongoDbContainer.StartAsync());
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

        private static async Task WaitForKafkaReadyAsync(string bootstrapServers, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                try
                {
                    var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
                    using var admin = new AdminClientBuilder(config).Build();
                    var metadata = admin.GetMetadata(TimeSpan.FromSeconds(1));

                    if (metadata.Brokers.Count != 0)
                    {
                        await Task.Delay(timeout);
                        Console.WriteLine("Kafka broker is ready.");
                        return;
                    }
                }
                catch
                {
                    // Ignore and retry
                }

                await Task.Delay(1000);
            }

            throw new TimeoutException("Kafka did not become ready in time.");
        }
    }
}
