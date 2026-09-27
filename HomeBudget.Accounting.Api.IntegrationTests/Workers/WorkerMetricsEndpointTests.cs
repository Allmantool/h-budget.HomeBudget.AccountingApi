using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

using HomeBudget.Core.Observability;

using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.Workers
{
    [TestFixture]
    public class WorkerMetricsEndpointTests
    {
        [Test]
        public async Task Metrics_WhenTraceExportIsDisabled_RemainsReachableAndExportsWorkerInstruments()
        {
            using var host = AccountingWorker.Program.CreateHost(
                [
                    "--urls=http://127.0.0.1:0",
                    "--ObservabilityOptions:TelemetryEndpoint=",
                    "--SeqOptions:IsEnabled=false",
                    "--ElasticSearchOptions:IsEnabled=false"
                ],
                services => RemoveApplicationWorkers(services),
                "Integration");

            await host.StartAsync();
            try
            {
                TelemetryMetrics.KafkaMessagesReceived.Add(1);

                var server = host.Services.GetRequiredService<IServer>();
                var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var httpClient = new HttpClient { BaseAddress = new Uri(address) };

                var response = await httpClient.GetStringAsync("/metrics");

                response.Should().Contain("homebudget_kafka_messages_received_total");
            }
            finally
            {
                await host.StopAsync();
            }
        }

        private static void RemoveApplicationWorkers(IServiceCollection services)
        {
            var descriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Namespace?.StartsWith(
                        "HomeBudget",
                        StringComparison.Ordinal) == true)
                .ToArray();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }
        }
    }
}
