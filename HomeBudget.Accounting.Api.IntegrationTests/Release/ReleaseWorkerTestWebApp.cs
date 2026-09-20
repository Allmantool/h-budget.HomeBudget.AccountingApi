using System;
using System.Threading.Tasks;

using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.Release
{
    internal sealed class ReleaseWorkerTestWebApp : BaseTestWebApp<Program, AccountingWorker.Program>
    {
        public ReleaseWorkerTestWebApp()
        {
            ShouldInitializeWebApp = false;
            ShouldInitializeWorkers = true;
            ShouldSuppressKafkaDiagnostics = true;
        }

        protected override async ValueTask DisposeAsyncCoreAsync()
        {
            // The NUnit fixture owns the shared Testcontainers service and resets it in OneTimeTearDown.
            await StopWorkersAsync();
        }
    }
}
