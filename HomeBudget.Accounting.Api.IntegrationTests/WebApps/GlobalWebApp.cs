using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class GlobalWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
    }
}
