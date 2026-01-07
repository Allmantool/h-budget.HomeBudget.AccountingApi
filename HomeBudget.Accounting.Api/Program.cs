using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Extensions;
using HomeBudget.Accounting.Api.Extensions.Logs;
using HomeBudget.Accounting.Api.Extensions.OpenTelemetry;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Extensions;
using HomeBudget.Components.Operations.MapperProfileConfigurations;

var webAppBuilder = WebApplication.CreateBuilder(args);
var webHost = webAppBuilder.WebHost;
var services = webAppBuilder.Services;
var environment = webAppBuilder.Environment;
var applicationName = environment.ApplicationName;
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

services
    .SetUpDi(configuration, environment)
    .AddEndpointsApiExplorer()
    .SetUpHealthCheck(configuration, Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    .AddResponseCaching()
    .AddSwaggerGen()
    .SetupSwaggerGen();

services.AddHeaderPropagation(options =>
{
    options.Headers.Add(HttpHeaderKeys.HostService, HostServiceOptions.AccountApiName);
    options.Headers.Add(HttpHeaderKeys.CorrelationId);
});

services.AddAutoMapper(cfg =>
{
    cfg.AddMaps(typeof(Program).Assembly);
    cfg.AddMaps(PaymentOperationsComponentMappingProfile.GetExecutingAssembly());
});

services.InitializeOpenTelemetry(environment);

services.AddLogging(loggerBuilder => configuration.InitializeLogger(environment, loggerBuilder, webAppBuilder.Host));

var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString();
var isTracingEnabled = services.TryAddTracingSupport(configuration, applicationName, serviceVersion);

webHost.AddAndConfigureSentry();

MongoEnumerationSerializerRegistration.RegisterAllBaseEnumerations(typeof(AccountTypes).Assembly);
var app = webAppBuilder.Build();

app.SetupHttpLogging();
app.SetupOpenTelemetry();

app.SetUpBaseApplication(services, environment, configuration);
app.UseAuthorization();
app.MapControllers();

try
{
    if (isTracingEnabled)
    {
        app.MapPrometheusScrapingEndpoint("/metrics");
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    app.Logger.LogError($"Fatal error: {ex}");
    Environment.Exit(1);
}

// To add visibility for integration tests
namespace HomeBudget.Accounting.Api
{
    public partial class Program
    {
        protected Program() { }
    }
}
