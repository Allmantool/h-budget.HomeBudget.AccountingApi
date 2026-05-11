using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal sealed class RedisHealthCheckRegistration(IOptions<DatabaseConnectionOptions> options)
        : IConfigureOptions<HealthCheckServiceOptions>
    {
        public void Configure(HealthCheckServiceOptions healthCheckOptions)
        {
            if (string.IsNullOrWhiteSpace(options.Value.RedisConnectionString))
            {
                return;
            }

            healthCheckOptions.Registrations.Add(new HealthCheckRegistration(
                "redis",
                serviceProvider => ActivatorUtilities.CreateInstance<RedisReadinessHealthCheck>(serviceProvider),
                HealthStatus.Unhealthy,
                new HashSet<string> { "ready", "redis" }));
        }
    }
}
