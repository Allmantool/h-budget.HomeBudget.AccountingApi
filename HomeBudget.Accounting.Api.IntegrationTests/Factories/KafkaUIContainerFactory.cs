using System;
using System.IO;
using System.Threading.Tasks;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Test.Core.Factories;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    internal static class KafkaUIContainerFactory
    {
        public static async Task<IContainer> BuildAsync(INetwork network)
        {
            var testContainersConfigPath = Path
                .GetFullPath(Path.Combine(AppContext.BaseDirectory, "Configs/dynamic_config.yaml"))
                .Replace('\\', '/');

            if (!File.Exists(testContainersConfigPath))
            {
                throw new FileNotFoundException($"Missing config: {testContainersConfigPath}");
            }

            return await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                         $"{nameof(TestContainersService)}-kafka-test-ui",
                         cb => cb.WithImage("provectuslabs/kafka-ui:v0.7.2")
                        .WithName($"{nameof(TestContainersService)}-kafka-test-ui-{Guid.NewGuid()}")
                        .WithHostname($"test-kafka-ui")
                        .WithPortBinding(8080, true)
                        .WithEnvironment("DYNAMIC_CONFIG_ENABLED", "true")

                        // .WithEnvironment("KAFKA_CLUSTERS_0_NAME", $"test-kafka-cluster")
                        // .WithEnvironment("KAFKA_CLUSTERS_0_PROPERTIES_BOOTSTRAP_SERVERS", "PLAINTEXT://test-kafka:9092")
                        // .WithEnvironment("KAFKA_CLUSTERS_0_PROPERTIES_CLIENT_DNS_LOOKUP", "use_all_dns_ips")
                        // .WithEnvironment("KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS", "test-kafka:9092")
                        // .WithEnvironment("KAFKA_CLUSTERS_0_PROPERTIES_METADATA_MAX_AGE_MS", "30000")
                        .WithEnvironment("SERVER_SERVLET_CONTEXT_PATH", "/")
                        .WithBindMount(testContainersConfigPath, "/etc/kafkaui/dynamic_config.yaml", AccessMode.ReadOnly)
                        .WithWaitStrategy(
                            Wait.ForUnixContainer()
                                .AddCustomWaitStrategy(
                                    new CustomWaitStrategy(TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes))
                                ))
                        .WithCreateParameterModifier(config =>
                        {
                            config.HostConfig.Memory = BaseTestContainerOptions.Memory1Gb;
                            config.HostConfig.NanoCPUs = BaseTestContainerOptions.NanoCPUs;
                            config.StopTimeout = TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes);
                        })
                        .WithNetwork(network)
                        .WithStartupCallback((kafkaContainer, cancelToken) =>
                        {
                            return Task.CompletedTask;
                        })
                        .WithAutoRemove(true)
                        .WithCleanUp(true)
                        .Build());
        }
    }
}
