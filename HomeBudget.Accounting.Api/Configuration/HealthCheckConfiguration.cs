using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using HomeBudget.Accounting.Api.Middlewares;
using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Extensions.Logs;

namespace HomeBudget.Accounting.Api.Configuration
{
    internal static class HealthCheckConfiguration
    {
        public static IServiceCollection SetUpHealthCheck(this IServiceCollection services, IConfiguration configuration, string hostUrls)
        {
            services
                .AddHealthChecks()
                .AddCheck("heartbeat", () => HealthCheckResult.Healthy())
                .AddCheck<CustomLogicHealthCheck>(nameof(CustomLogicHealthCheck), tags: new[] { "custom" });

            services.AddHealthChecksUI(setupSettings: setup =>
            {
                setup.AddHealthCheckEndpoint("[Currency rates endpoint]", configuration.GetHealthCheckEndpoint(hostUrls));
            }).AddInMemoryStorage();

            return services;
        }

        public static IApplicationBuilder SetUpHealthCheckEndpoints(this IApplicationBuilder builder)
        {
            return builder.UseEndpoints(config =>
            {
                config.MapHealthChecks(Endpoints.HealthCheckSource, new HealthCheckOptions
                {
                    Predicate = _ => true,
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
