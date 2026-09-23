using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class CrossAccountsTransferWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
        public CrossAccountsTransferWebApp(
            System.Action<Microsoft.Extensions.DependencyInjection.IServiceCollection> configureTestServices = null,
            bool initializeWorkers = true)
        {
            ConfigureTestServices = configureTestServices;
            ShouldInitializeWorkers = initializeWorkers;
        }
    }
}
