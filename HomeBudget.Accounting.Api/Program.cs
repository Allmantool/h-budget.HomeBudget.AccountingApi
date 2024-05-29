using System;
using System.Collections.Generic;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Extensions;
using HomeBudget.Accounting.Api.Extensions.Logs;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Components.Operations.MapperProfileConfigurations;

var webAppBuilder = WebApplication.CreateBuilder(args);
var services = webAppBuilder.Services;
var environment = webAppBuilder.Environment;
var configuration = webAppBuilder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true)
    .Build();

services
    .AddControllers(o =>
    {
        o.Conventions.Add(new SwaggerControllerDocConvention());
    })
    .AddJsonOptions(configure =>
    {
        configure.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

services.SetUpDi(configuration, environment);

webAppBuilder.Services.AddEndpointsApiExplorer();
webAppBuilder.Services
    .SetUpHealthCheck(configuration, Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    .AddResponseCaching()
    .AddSwaggerGen();

services.SetupSwaggerGen();

services.AddHeaderPropagation(options =>
{
    options.Headers.Add(HttpHeaderKeys.HostService, HostServiceOptions.Name);
    options.Headers.Add(HttpHeaderKeys.CorrelationId);
});

services.AddAutoMapper(new List<Assembly>
{
    typeof(Program).Assembly,
    PaymentOperationsComponentMappingProfile.GetExecutingAssembly(),
});

services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: environment.ApplicationName))
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
            .SetMaxMetricPointsPerMetricStream(OpenTelemetryOptions.MaxMetricPointsPerMetricStream)
            .AddPrometheusExporter()
    );

configuration.InitializeLogger(environment, webAppBuilder.Host);

var webApp = webAppBuilder.Build();

webApp.UseOpenTelemetryPrometheusScrapingEndpoint();

webApp.SetUpBaseApplication(services, environment, configuration);
webApp.UseAuthorization();
webApp.MapControllers();

webApp.Run();

// To add visibility for integration tests
namespace HomeBudget.Accounting.Api
{
    public partial class Program
    {
        protected Program() { }
    }
}
