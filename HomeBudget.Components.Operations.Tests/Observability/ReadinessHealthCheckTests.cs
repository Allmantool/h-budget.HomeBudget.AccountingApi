using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.HealthChecks;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Tests.Observability
{
    [TestFixture]
    public class ReadinessHealthCheckTests
    {
        [Test]
        public async Task SqlServerReadiness_WhenConnectionStringMissing_ShouldBeDegraded()
        {
            var check = new SqlServerReadinessHealthCheck(
                Microsoft.Extensions.Options.Options.Create(new DatabaseConnectionOptions()));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Degraded);
        }

        [Test]
        public async Task RedisReadiness_WhenEndpointCannotBeParsed_ShouldBeUnhealthy()
        {
            var check = new RedisReadinessHealthCheck(
                Microsoft.Extensions.Options.Options.Create(new DatabaseConnectionOptions
                {
                    RedisConnectionString = ":not-a-port"
                }));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Unhealthy);
        }
    }
}
