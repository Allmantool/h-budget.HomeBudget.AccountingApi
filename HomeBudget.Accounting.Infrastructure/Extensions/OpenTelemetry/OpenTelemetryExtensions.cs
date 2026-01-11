using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Core;
using HomeBudget.Core.Constants;

namespace HomeBudget.Accounting.Infrastructure.Extensions
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

            var applicationName = environment.ApplicationName;

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

            services
                .AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(
                       serviceName: serviceName,
                       serviceVersion: serviceVersion,
                       serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        [OpenTelemetryTags.DeploymentEnvironment] = environment.EnvironmentName
                    }))
                .WithTracing(traceBuilder =>
                {
                    traceBuilder
                        .AddSource(Observability.ActivitySourceName)
                        .AddAspNetCoreInstrumentation(options =>
                        {
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
                         .AddHttpClientInstrumentation()
                         .AddSource(HostServiceOptions.AccountingApiName)
                         .AddOtlpExporter(o =>
                         {
                             o.Endpoint = new Uri(alloyHost);
                             o.Protocol = OtlpExportProtocol.Grpc;
                         });
                })
                .ConfigureResource(resource => resource.AddService(serviceName: environment.ApplicationName))
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
                    .SetMaxMetricStreams(OpenTelemetryOptions.MaxMetricStreams)
                    .AddPrometheusExporter()
                );

            return true;
        }
    }
}
