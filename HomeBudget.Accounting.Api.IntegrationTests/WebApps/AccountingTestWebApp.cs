using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class AccountingTestWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
        public AccountingTestWebApp()
        {
            ShouldInitializeWebApp = true;
            ShouldInitializeWorkers = true;
        }
    }
}
