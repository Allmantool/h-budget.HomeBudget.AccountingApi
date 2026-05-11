using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal sealed class SqlServerReadinessHealthCheck(IOptions<DatabaseConnectionOptions> options)
        : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var connectionString = options.Value.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return HealthCheckResult.Degraded("SQL Server connection string is not configured.");
            }

            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = 5;
                await command.ExecuteScalarAsync(cancellationToken);

                return HealthCheckResult.Healthy("SQL Server is reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("SQL Server readiness probe failed.", ex);
            }
        }
    }
}
