using System.Threading;
using System.Threading.Tasks;

using DotNet.Testcontainers.Networks;

namespace HomeBudget.Accounting.Api.IntegrationTests.Factories.Models
{
    internal class ExistingTestContainersNetwork : INetwork
    {
        public string Id { get; }
        public string Name { get; }

        public ExistingTestContainersNetwork(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public Task CreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
