using System;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using HomeBudget.Accounting.Infrastructure.Constants;

namespace HomeBudget.Accounting.Api.Extensions.OpenTelemetry
{
    internal static class OpenTelemetryExtension
    {
        public static IServiceCollection InitializeOpenTelemetry(this IServiceCollection services, IWebHostEnvironment environment)
        {
            services
                .AddOpenTelemetry()
                .ConfigureResource(resource =>
                    resource.AddService(
                        serviceName: environment.ApplicationName,
                        serviceVersion: Assembly
                            .GetExecutingAssembly()
                            .GetName()
                            .Version?
                            .ToString(),
                        serviceInstanceId: Environment.MachineName
                ))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .SetSampler(new AlwaysOnSampler())
                    .AddOtlpExporter()
                )
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Routing")
                    .AddMeter("Microsoft.AspNetCore.Diagnostics")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddMeter("Microsoft.AspNetCore.Http.Connections")
                    .AddMeter("Microsoft.Extensions.Diagnostics.HealthChecks")
                    .SetMaxMetricStreams(OpenTelemetryOptions.MaxMetricStreams)
                    .AddPrometheusExporter());

            return services;
        }

        public static WebApplication SetupOpenTelemetry(this WebApplication app)
        {
            app.UseOpenTelemetryPrometheusScrapingEndpoint();

            return app;
        }
    }
}
