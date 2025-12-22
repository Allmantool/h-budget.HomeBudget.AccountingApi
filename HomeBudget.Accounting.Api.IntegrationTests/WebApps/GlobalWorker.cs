using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class GlobalWorker : BaseTestWebApp<Program, AccountingWorker.Program>
    {
    }
}
