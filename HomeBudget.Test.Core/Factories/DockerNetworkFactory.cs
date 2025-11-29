using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Networks;

using HomeBudget.Test.Core.Models;

namespace HomeBudget.Test.Core.Factories
{
    public static class DockerNetworkFactory
    {
        public static async Task<INetwork> GetOrCreateDockerNetworkAsync(string networkName)
        {
            using var dockerClient = new DockerClientConfiguration().CreateClient();

            var networks = await dockerClient.Networks.ListNetworksAsync(
                new NetworksListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        {
                            "name",
                            new Dictionary<string, bool>
                            {
                                { networkName, true }
                            }
                        }
                    }
                });

            var existingNetwork = networks.FirstOrDefault(n => n.Name == networkName);
            if (existingNetwork is not null)
            {
                return new ExistingTestContainersNetwork(existingNetwork.ID, existingNetwork.Name);
            }

            try
            {
                var newNetwork = new NetworkBuilder()
                    .WithName(networkName)
                    .WithDriver(NetworkDriver.Bridge)
                    .Build();

                await newNetwork.CreateAsync();
                return newNetwork;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
