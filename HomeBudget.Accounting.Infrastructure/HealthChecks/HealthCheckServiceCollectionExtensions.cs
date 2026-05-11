using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    public static class HealthCheckServiceCollectionExtensions
    {
        public static IHealthChecksBuilder AddAccountingReadinessChecks(this IHealthChecksBuilder builder)
        {
            builder
                .AddCheck<SqlServerReadinessHealthCheck>(
                    "sql-server",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["ready", "sqlserver"])
                .AddCheck<MongoDbReadinessHealthCheck>(
                    "mongodb",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["ready", "mongodb"])
                .AddCheck<KafkaReadinessHealthCheck>(
                    "kafka",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["ready", "kafka"])
                .AddCheck<EventStoreDbReadinessHealthCheck>(
                    "eventstoredb",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["ready", "eventstoredb"]);

            builder.Services.AddOptions<DatabaseConnectionOptions>();
            builder.Services.AddSingleton<IConfigureOptions<HealthCheckServiceOptions>, RedisHealthCheckRegistration>();

            return builder;
        }
    }
}
