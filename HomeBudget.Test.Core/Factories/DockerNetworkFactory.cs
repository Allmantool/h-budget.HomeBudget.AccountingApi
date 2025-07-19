﻿using System.Linq;
using System.Threading.Tasks;

using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;

using HomeBudget.Accounting.Api.IntegrationTests.Factories.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories
{
    public static class DockerNetworkFactory
    {
        public static async Task<INetwork> GetOrCreateDockerNetworkAsync(string networkName)
        {
            using var client = new DockerClientConfiguration().CreateClient();

            var networks = await client.Networks.ListNetworksAsync();

            var existingNetwork = networks.FirstOrDefault(n => n.Name == networkName);
            if (existingNetwork is not null)
            {
                return new ExistingTestContainersNetwork(existingNetwork.ID, existingNetwork.Name);
            }

            var newNetwork = new NetworkBuilder()
                .WithName(networkName)
                .Build();

            await newNetwork.CreateAsync();
            return newNetwork;
        }
    }
}
