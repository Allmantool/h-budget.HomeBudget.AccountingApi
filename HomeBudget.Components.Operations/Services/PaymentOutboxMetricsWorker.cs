using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Operations.Services
{
    internal sealed class PaymentOutboxMetricsWorker(
        ILogger<PaymentOutboxMetricsWorker> logger,
        IServiceScopeFactory scopeFactory)
        : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Unable to refresh payment outbox metrics.");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        internal async Task RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            const string sql = @"
                SELECT
                    CAST(SUM(CASE WHEN Status = @PendingStatus THEN 1 ELSE 0 END) AS bigint) AS PendingCount,
                    CAST(SUM(CASE WHEN Status IN (@FailedStatus, @DeadLetterStatus) THEN 1 ELSE 0 END) AS bigint) AS FailedDeadLetterCount,
                    COALESCE(DATEDIFF(second, MIN(CASE WHEN Status = @PendingStatus THEN CreatedUtc ELSE NULL END), SYSUTCDATETIME()), 0) AS OldestPendingAgeSeconds
                FROM dbo.OutboxAccountPayments;";

            await using var scope = scopeFactory.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IBaseReadRepository>();
            var snapshots = await reader.GetAsync<OutboxMetricSnapshot>(
                sql,
                new
                {
                    PendingStatus = OutboxStatus.Pending.Key,
                    FailedStatus = OutboxStatus.Failed.Key,
                    DeadLetterStatus = OutboxStatus.DeadLettered.Key
                });

            var snapshot = snapshots.FirstOrDefault() ?? new OutboxMetricSnapshot();
            TelemetryMetrics.SetOutboxStats(
                snapshot.PendingCount,
                snapshot.FailedDeadLetterCount,
                snapshot.OldestPendingAgeSeconds);
        }
    }
}
