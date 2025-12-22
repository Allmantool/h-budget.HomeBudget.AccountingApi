using HomeBudget.Accounting.Api.IntegrationTests.WebApps;

using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.Workers
{
    internal class EventStoreDbPaymentsConsumerTestWorker : BaseTestWebApp<Program, AccountingWorker.Program>
    {
        public EventStoreDbPaymentsConsumerTestWorker()
        {
            ShouldInitializeWebApp = false;
            ShouldInitializeWorkers = true;
        }
    }
}
