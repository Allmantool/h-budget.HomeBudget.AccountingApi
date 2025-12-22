using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal class ContractorsTestWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
    }
}
