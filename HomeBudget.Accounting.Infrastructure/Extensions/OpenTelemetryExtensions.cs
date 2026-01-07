using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HomeBudget.Accounting.Infrastructure.Extensions
{
    public static class OpenTelemetryExtensions
    {
        public static bool TryAddTracingSupport(
            this IServiceCollection services,
            IConfigurationRoot configuration,
            string applicationName,
            string serviceVersion)
        {
            var alloyHost = configuration.GetValue<string>("GrafanaOptions:AlloyHost");

            if (string.IsNullOrWhiteSpace(alloyHost))
            {
                return false;
            }

            services
               .AddOpenTelemetry()
               .ConfigureResource(r => r
                   .AddService(
                       serviceName: applicationName,
                       serviceVersion: serviceVersion,
                       serviceInstanceId: Environment.MachineName))
               .WithMetrics(m =>
               {
                   m.AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();
               })
               .WithTracing(t =>
               {
                   t.AddHttpClientInstrumentation()
                    .AddSource(applicationName)
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(alloyHost);
                    });
               });

            return true;
        }
    }
}
