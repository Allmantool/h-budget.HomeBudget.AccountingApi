using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal sealed class RedisReadinessHealthCheck(IOptions<DatabaseConnectionOptions> options)
        : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var endpoint = options.Value.RedisConnectionString;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return HealthCheckResult.Degraded("Redis connection string is not configured.");
            }

            if (!TryParseEndpoint(endpoint, out var host, out var port))
            {
                return HealthCheckResult.Unhealthy("Redis connection string endpoint could not be parsed.");
            }

            try
            {
                await TcpHealthProbe.ProbeAsync(host, port, TimeSpan.FromSeconds(5), cancellationToken);
                return HealthCheckResult.Healthy("Redis endpoint is reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis readiness probe failed.", ex);
            }
        }

        private static bool TryParseEndpoint(string connectionString, out string host, out int port)
        {
            host = null;
            port = 6379;

            var endpoint = connectionString.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            var parts = endpoint.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                return false;
            }

            host = parts[0];
            return parts.Length == 1
                   || int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
        }
    }
}
