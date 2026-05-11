using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal sealed class KafkaReadinessHealthCheck(
        IAdminClient adminClient,
        IOptions<KafkaOptions> options)
        : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var bootstrapServers = options.Value.AdminSettings?.BootstrapServers
                                   ?? options.Value.ConsumerSettings?.BootstrapServers
                                   ?? options.Value.ProducerSettings?.BootstrapServers;

            if (string.IsNullOrWhiteSpace(bootstrapServers))
            {
                return Task.FromResult(HealthCheckResult.Degraded("Kafka bootstrap servers are not configured."));
            }

            try
            {
                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
                return metadata.Brokers.Any()
                    ? Task.FromResult(HealthCheckResult.Healthy("Kafka brokers are reachable."))
                    : Task.FromResult(HealthCheckResult.Unhealthy("Kafka metadata contains no brokers."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Kafka readiness probe failed.", ex));
            }
        }
    }
}
