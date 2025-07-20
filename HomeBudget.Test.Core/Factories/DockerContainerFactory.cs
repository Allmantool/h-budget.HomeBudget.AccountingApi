using System;
using System.Linq;
using System.Threading.Tasks;

using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using HomeBudget.Test.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    public static class DockerContainerFactory
    {
        public static async Task<IContainer> GetOrCreateDockerContainerAsync(
            string containerName,
            Func<ContainerBuilder, IContainer> configureBuilder)
        {
            using var client = new DockerClientConfiguration().CreateClient();

            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true });

            var existingContainer = containers.FirstOrDefault(c =>
                c.Names.Any(n => n.TrimStart('/').Equals(containerName, StringComparison.Ordinal)));

            if (existingContainer is not null)
            {
                Console.WriteLine($"[Docker] Reusing existing container: {containerName} ({existingContainer.ID})");
                return new ExistingTestContainer(existingContainer.ID, containerName);
            }

            var builder = new ContainerBuilder()
                .WithName(containerName);

            return configureBuilder(builder);
        }
    }
}
