using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class CrossAccountsTransferWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
    }
}
