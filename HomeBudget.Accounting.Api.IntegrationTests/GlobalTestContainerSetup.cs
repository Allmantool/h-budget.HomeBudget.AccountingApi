using System.Threading.Tasks;

using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.WebApps;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    [SetUpFixture]
    public class GlobalTestContainerSetup
    {
        private GlobalWebApp _webAppSut;
        private GlobalWorker _workerSut;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            _webAppSut = new GlobalWebApp();
            _workerSut = new GlobalWorker();

            await Task.WhenAll(BaseTestWebApp<Program, Workers.OperationsConsumer.Program>.StartContainersAsync(), BaseTestWebApp<Program, Workers.OperationsConsumer.Program>.StartContainersAsync());
        }

        [OneTimeTearDown]
        public async Task TeardownAsync()
        {
            if (_webAppSut != null)
            {
                await _webAppSut.DisposeAsync();
            }

            if (_workerSut != null)
            {
                await _workerSut.DisposeAsync();
            }
        }
    }
}
