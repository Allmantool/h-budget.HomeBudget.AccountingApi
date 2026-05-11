using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal sealed class EventStoreDbReadinessHealthCheck(IOptions<EventStoreDbOptions> options)
        : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var url = options.Value.Url;
            if (url is null || string.IsNullOrWhiteSpace(url.Host))
            {
                return HealthCheckResult.Degraded("EventStoreDB URL is not configured.");
            }

            try
            {
                await TcpHealthProbe.ProbeAsync(url.Host, url.Port, TimeSpan.FromSeconds(5), cancellationToken);
                return HealthCheckResult.Healthy("EventStoreDB endpoint is reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("EventStoreDB readiness probe failed.", ex);
            }
        }
    }
}
