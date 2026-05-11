using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Extensions.Logs;
using HomeBudget.Accounting.Api.Middlewares;
using HomeBudget.Accounting.Infrastructure.Extensions;
using HomeBudget.Accounting.Infrastructure.HealthChecks;

namespace HomeBudget.Accounting.Api.Configuration
{
    internal static class HealthCheckConfiguration
    {
        public static IServiceCollection SetUpHealthCheck(
            this IServiceCollection services,
            IConfiguration configuration,
            string hostUrls)
        {
            services
                .AddHealthChecks()
                .AddCheck("heartbeat", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck<CustomLogicHealthCheck>(nameof(CustomLogicHealthCheck), tags: ["custom", "ready"])
                .AddAccountingReadinessChecks();

            services.AddHealthChecksUI(setupSettings: setup =>
            {
                setup.AddHealthCheckEndpoint("[Accounting endpoint]", configuration.GetHealthCheckEndpoint(hostUrls));
            }).AddInMemoryStorage();

            return services;
        }

        public static IApplicationBuilder SetUpHealthCheckEndpoints(this IApplicationBuilder builder, IWebHostEnvironment webHostEnvironment)
        {
            if (webHostEnvironment.IsIntegrationTesting())
            {
                return builder;
            }

            return builder.UseEndpoints(config =>
            {
                config.MapHealthChecks(Endpoints.HealthCheckSource, new HealthCheckOptions
                {
                    Predicate = _ => true,
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
                });

                config.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("live"),
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
                });

                config.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("ready"),
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
                });

                config.MapHealthChecksUI(options =>
                {
                    options.UIPath = "/show-health-ui";
                    options.ApiPath = "/health-ui-api";
                });
            });
        }
    }
}
