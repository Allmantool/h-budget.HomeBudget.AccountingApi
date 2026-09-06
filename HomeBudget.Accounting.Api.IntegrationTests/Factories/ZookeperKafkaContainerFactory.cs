using System.Threading.Tasks;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Test.Core.Factories;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    internal class ZookeperKafkaContainerFactory
    {
        public static async Task<IContainer> BuildAsync(INetwork network)
        {
            var containerName = $"{nameof(TestContainersService)}-test-zookeper-{System.Guid.NewGuid():N}";

            return await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                    containerName,
                    cb => cb
                    .WithImage("confluentinc/cp-zookeeper:7.9.0")
                    .WithName(containerName)
                    .WithHostname(TestContainerHostNames.ZK)
                    .WithEnvironment("ZOOKEEPER_CLIENT_PORT", "2181")
                    .WithEnvironment("ZOOKEEPER_TICK_TIME", "2000")
                    .WithWaitStrategy(Wait.ForUnixContainer())
                    .WithNetwork(network)
                    .WithAutoRemove(true)
                    .WithCleanUp(true)
                    .Build());
        }
    }
}
