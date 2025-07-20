using System.Threading.Tasks;

using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.WebApps;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    [SetUpFixture]
    public class GlobalTestContainerSetup
    {
        private GlobalWebApp _sut;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            _sut = new GlobalWebApp();
            await _sut.StartAsync();
        }

        [OneTimeTearDown]
        public async Task TeardownAsync()
        {
            if (_sut != null)
            {
                await _sut.DisposeAsync();
            }
        }
    }
}
