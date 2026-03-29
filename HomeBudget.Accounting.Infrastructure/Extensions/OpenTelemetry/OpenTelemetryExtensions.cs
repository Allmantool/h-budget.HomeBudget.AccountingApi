using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Observability;

namespace HomeBudget.Accounting.Infrastructure.Extensions.OpenTelemetry
{
    public static class OpenTelemetryExtensions
    {
        public static bool TryAddTracingSupport(
            this IServiceCollection services,
            IConfigurationRoot configuration,
            IWebHostEnvironment environment,
            string serviceName,
            string serviceVersion)
        {
            var alloyHost = configuration.GetValue<string>("ObservabilityOptions:TelemetryEndpoint");

            if (string.IsNullOrWhiteSpace(alloyHost))
            {
                return false;
            }

            services
               .AddOpenTelemetry()
               .ConfigureResource(r => r
                   .AddService(
                       serviceName: serviceName,
                       serviceVersion: serviceVersion,
                       serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["service.namespace"] = "HomeBudget",
                        [OpenTelemetryTags.DeploymentEnvironment] = environment.EnvironmentName
                    }))
                .WithTracing(traceBuilder =>
                {
                    traceBuilder
                        .AddSource(Telemetry.ActivitySource.Name)
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = httpContext =>
                            {
                                var path = httpContext.Request.Path;
                                return !path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                                       && !path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);
                            };

                            options.EnrichWithHttpRequest = (activity, request) =>
                            {
                                if (request.Headers.TryGetValue(HttpHeaderKeys.CorrelationId, out var cid))
                                {
                                    activity.SetTag(ActivityTags.CorrelationId, cid.ToString());
                                }
                            };

                            options.EnrichWithHttpResponse = (activity, response) =>
                            {
                                activity.SetTag(ActivityTags.HttpStatusCode, response.StatusCode);
                            };

                            options.EnrichWithException = (activity, exception) =>
                            {
                                activity.SetTag(ActivityTags.ExceptionMessage, exception.Message);
                            };
                        })
                         .AddHttpClientInstrumentation(options =>
                         {
                             options.RecordException = true;
                         })
                         .AddOtlpExporter(o =>
                         {
                             o.Endpoint = new Uri(alloyHost);
                             o.Protocol = OtlpExportProtocol.Grpc;
                         });
                })
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(MetersTags.Hosting)
                    .AddMeter(MetersTags.Routing)
                    .AddMeter(MetersTags.Diagnostics)
                    .AddMeter(MetersTags.Kestrel)
                    .AddMeter(MetersTags.HttpConnections)
                    .AddMeter(MetersTags.HealthChecks)
                    .AddMeter(TelemetryMetrics.Meter.Name)
                    .SetMaxMetricStreams(OpenTelemetryOptions.MaxMetricStreams)
                    .AddPrometheusExporter()
                );

            return true;
        }

        public static IApplicationBuilder SetupOpenTelemetry(this IApplicationBuilder app)
        {
            app.UseOpenTelemetryPrometheusScrapingEndpoint();

            return app;
        }
    }
}
