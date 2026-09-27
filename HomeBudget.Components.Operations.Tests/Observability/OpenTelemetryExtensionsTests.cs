using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using OpenTelemetry.Metrics;

using HomeBudget.Accounting.Infrastructure.Extensions.OpenTelemetry;

namespace HomeBudget.Components.Operations.Tests.Observability
{
    [TestFixture]
    public class OpenTelemetryExtensionsTests
    {
        [Test]
        public void TryAddTracingSupport_WhenTraceEndpointIsMissing_ThenStillRegistersMetrics()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection()
                .Build();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(value => value.EnvironmentName).Returns("Test");
            var services = new ServiceCollection();

            var tracingEnabled = services.TryAddTracingSupport(
                configuration,
                environment.Object,
                "homebudget-accounting-payments-consumer-worker",
                "1.2.3");

            using var provider = services.BuildServiceProvider();
            tracingEnabled.Should().BeFalse();
            provider.GetService<MeterProvider>().Should().NotBeNull();
        }
    }
}
