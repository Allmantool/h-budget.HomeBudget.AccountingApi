using System.Threading.Tasks;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

using HomeBudget.Test.Core.Factories;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    internal class ZookeperKafkaContainerFactory
    {
        public static async Task<IContainer> BuildWithKraftModeAsync(INetwork network)
        {
            return await DockerContainerFactory.GetOrCreateDockerContainerAsync(
                    $"{nameof(TestContainersService)}-test-zookeper",
                    cb => cb
                    .WithImage("confluentinc/cp-zookeeper:7.9.0")
                    .WithName($"{nameof(TestContainersService)}-test-zookeper")
                    .WithHostname("test-zookeper")
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
