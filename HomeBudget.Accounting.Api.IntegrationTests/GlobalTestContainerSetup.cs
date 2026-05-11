using System.Threading.Tasks;

using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.WebApps;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    [SetUpFixture]
    public class GlobalTestContainerSetup
    {
        private TestContainersService _testContainersService;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            _testContainersService = await TestContainersService.InitAsync();
        }

        [OneTimeTearDown]
        public async Task TeardownAsync()
        {
            if (_testContainersService != null)
            {
                await _testContainersService.DisposeAsync();
            }
        }
    }
}
