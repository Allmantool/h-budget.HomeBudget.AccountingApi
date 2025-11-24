using System;
using System.IO;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    internal static class KafkaContainerFactory
    {
        public static KafkaContainer Build(INetwork network)
        {
            var testcontainersScriptPath = Path
                .GetFullPath(Path.Combine(AppContext.BaseDirectory, "Scripts/testcontainers.sh"))
                .Replace('\\', '/');

            if (!File.Exists(testcontainersScriptPath))
            {
                throw new FileNotFoundException($"Missing script: {testcontainersScriptPath}");
            }

            var kafkaConfigPath = Path
                .GetFullPath(Path.Combine(AppContext.BaseDirectory, "Configs/server.properties"))
                .Replace('\\', '/');

            if (!File.Exists(kafkaConfigPath))
            {
                throw new FileNotFoundException($"Missing config: {kafkaConfigPath}");
            }

            return new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:8.1.0")
                .WithName($"{nameof(TestContainersService)}-kafka-container-{Guid.NewGuid()}")
                .WithHostname("test-kafka")

                // .WithPortBinding(29092, true)
                // .WithPortBinding(9092, true)
                // .WithPortBinding(9093, true)
                // .WithPortBinding(9094, true)
                // .WithPortBinding(9997, true)
                .WithPortBinding(29092, 29092)
                .WithPortBinding(9092, 9092)
                .WithPortBinding(9093, 9093)
                .WithPortBinding(9094, 9094)
                .WithPortBinding(9997, 9997)

                // v8+ KRAFT_MODE
                .WithEnvironment("KAFKA_KRAFT_MODE", "true")
                .WithEnvironment("KAFKA_NODE_ID", "1")
                .WithEnvironment("CLUSTER_ID", "5Y7pZQq4Td6Jv4n3z2Z8Zg")
                .WithEnvironment("KAFKA_CLUSTER_ID", "5Y7pZQq4Td6Jv4n3z2Z8Zg")
                .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
                .WithEnvironment(
                    "KAFKA_LISTENERS",
                    "PLAINTEXT://0.0.0.0:29092,PLAINTEXT_HOST://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093,BROKER://0.0.0.0:9094")
                    .WithEnvironment(
                    "KAFKA_ADVERTISED_LISTENERS",
                    "PLAINTEXT://test-kafka:29092,PLAINTEXT_HOST://localhost:9092,BROKER://test-kafka:9094")
                    .WithEnvironment(
                    "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                    "PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT,CONTROLLER:PLAINTEXT,BROKER:PLAINTEXT")

                // .WithEnvironment("INITIAL_CONTROLLERS", "1@test-kafka:9093")
                .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@test-kafka:9093")
                .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
                .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "PLAINTEXT")
                .WithEnvironment("JMX_PORT", "9997")
                .WithEnvironment("KAFKA_JMX_PORT", "9997")
                .WithEnvironment("KAFKA_JMX_HOSTNAME", "test-kafka")
                .WithEnvironment("KAFKA_LOG_DIRS", "/tmp/kraft-combined-logs")
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
                .WithEnvironment("KAFKA_SOCKET_REQUEST_MAX_BYTES", "1000000000")
                .WithEnvironment("KAFKA_MESSAGE_MAX_BYTES", "2147483647")
                .WithEnvironment("KAFKA_REPLICA_FETCH_MAX_BYTES", "2147483647")
                .WithNetwork(network)
                .WithBindMount(testcontainersScriptPath, "/testcontainers.sh", AccessMode.ReadOnly)

                // .WithBindMount(kafkaConfigPath, "/etc/kafka/server.properties", AccessMode.ReadOnly)
                .WithWaitStrategy(
                    Wait.ForUnixContainer()
                        .AddCustomWaitStrategy(
                            new CustomWaitStrategy(TimeSpan.FromMinutes(5))
                        ))
                .WithCreateParameterModifier(config =>
                {
                    config.HostConfig.Memory = BaseTestContainerOptions.Memory;
                    config.HostConfig.NanoCPUs = BaseTestContainerOptions.NanoCPUs;
                    config.StopTimeout = TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes);
                })
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .Build();
        }
    }
}
