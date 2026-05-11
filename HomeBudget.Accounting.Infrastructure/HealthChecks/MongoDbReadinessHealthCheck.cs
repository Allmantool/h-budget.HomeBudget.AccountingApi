using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal sealed class MongoDbReadinessHealthCheck(IOptions<MongoDbOptions> options)
        : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var mongoOptions = options.Value;
            if (string.IsNullOrWhiteSpace(mongoOptions.ConnectionString))
            {
                return HealthCheckResult.Degraded("MongoDB connection string is not configured.");
            }

            try
            {
                var client = new MongoClient(mongoOptions.ConnectionString);
                var databaseName = string.IsNullOrWhiteSpace(mongoOptions.LedgerDatabase)
                    ? "admin"
                    : mongoOptions.LedgerDatabase;
                var database = client.GetDatabase(databaseName);
                await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: cancellationToken);

                return HealthCheckResult.Healthy("MongoDB is reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("MongoDB readiness probe failed.", ex);
            }
        }
    }
}
