using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Services
{
    internal sealed class ProjectionRebuildWorker(
        ProjectionRebuildRunner runner,
        IHostApplicationLifetime applicationLifetime,
        ILogger<ProjectionRebuildWorker> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Projection rebuild terminated without completing its requested mode.");
                Environment.ExitCode = 1;
            }
            finally
            {
                applicationLifetime.StopApplication();
            }
        }
    }
}
