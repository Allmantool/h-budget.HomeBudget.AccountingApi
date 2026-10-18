using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Triggers
{
    internal sealed class RecoveryTriggerScheduler(
        RecoveryTriggerCoordinator coordinator,
        IRecoveryTriggerTargetSource targetSource,
        IOptions<RecoveryTriggerOptions> options,
        TimeProvider timeProvider) : BackgroundService
    {
        internal async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            var targets = await targetSource.GetTargetsAsync(cancellationToken).ConfigureAwait(false);
            var nowUtc = timeProvider.GetUtcNow();
            foreach (var target in targets)
            {
                await coordinator.TriggerScheduledAsync(target, nowUtc, cancellationToken).ConfigureAwait(false);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.Value.SchedulerPollInterval, timeProvider, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
