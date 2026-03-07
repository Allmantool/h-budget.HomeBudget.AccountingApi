using System.Threading.Tasks;

using Testcontainers.MsSql;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class MsSqlDbContainerExtensions
    {
        public static async Task ResetContainersAsync(this MsSqlContainer container)
        {
        }
    }
}
