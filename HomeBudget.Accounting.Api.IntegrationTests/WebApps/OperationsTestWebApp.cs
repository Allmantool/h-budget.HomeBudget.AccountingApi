using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class OperationsTestWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
    }
}
