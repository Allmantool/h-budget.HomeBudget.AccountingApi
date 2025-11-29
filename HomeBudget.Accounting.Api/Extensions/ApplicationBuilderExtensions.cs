using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Middlewares;
using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.Extensions
{
    internal static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder SetUpBaseApplication(
            this IApplicationBuilder app,
            IServiceCollection services,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            app.SetUpSwaggerUi();

            Log.Information("Current env is '{0}'.", env.EnvironmentName);

            if (env.IsUnderDevelopment())
            {
                app.UseDeveloperExceptionPage();

                app.UseCors(corsPolicyBuilder =>
                {
                    var allowedUiOrigins = configuration.GetSection("UiOriginsUrl").Get<string[]>();

                    Log.Information("UI origin is '{0}'", string.Join(" ,", allowedUiOrigins));

                    corsPolicyBuilder
                        .WithOrigins(allowedUiOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .WithExposedHeaders(HttpHeaderKeys.CorrelationId);
                });
            }

            return app
                .UseHsts()
                .UseHttpsRedirection()
                .UseResponseCaching()
                .UseAuthorization()
                .UseCorrelationId()
                .UseHeaderPropagation()
                .UseRouting()
                .UseSerilogRequestLogging(options =>
                {
                    // Customize the message template
                    options.MessageTemplate = "Handled {RequestPath}";

                    // Emit debug-level events instead of the defaults
                    options.GetLevel = (_, _, _) => LogEventLevel.Debug;

                    // Attach additional properties to the request completion event
                    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                    {
                        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                    };
                })
                .SetUpHealthCheckEndpoints(env)
                .UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}
