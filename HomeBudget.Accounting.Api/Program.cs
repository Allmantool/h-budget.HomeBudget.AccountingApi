using System;

using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Extensions;
using HomeBudget.Accounting.Api.Filters;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Extensions;
using HomeBudget.Accounting.Infrastructure.Extensions.Logs;
using HomeBudget.Accounting.Infrastructure.Extensions.OpenTelemetry;
using HomeBudget.Accounting.Notifications.Configuration;
using HomeBudget.Components.Operations.MapperProfileConfigurations;
using HomeBudget.Core.Models;

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
        o.Filters.Add<ResultToHttpStatusFilter>();
    })
    .AddJsonOptions(configure =>
    {
        configure.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value.Errors.Select(error => FormatModelStateError(entry.Key, error.ErrorMessage)))
                .ToList();

            var message = errors.Count == 0
                ? "Validation failed"
                : $"Validation failed: {string.Join("; ", errors)}";

            return new BadRequestObjectResult(Result<object>.Failure(message));
        };
    });

services
    .SetUpDi(configuration, environment)
    .AddEndpointsApiExplorer()
    .AddResponseCaching()
    .AddSwaggerGen()
    .SetupSwaggerGen();

if (!environment.IsIntegrationTesting())
{
    services.SetUpHealthCheck(configuration, Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
}

services.AddHeaderPropagation(options =>
{
    options.Headers.Add(HttpHeaderKeys.HostService, HostServiceOptions.AccountingApiName);
    options.Headers.Add(HttpHeaderKeys.CorrelationId);
    options.Headers.Add("traceparent");
    options.Headers.Add("tracestate");
    options.Headers.Add("baggage");
});

services.AddAutoMapper(cfg =>
{
    cfg.AddMaps(typeof(Program).Assembly);
    cfg.AddMaps(PaymentOperationsComponentMappingProfile.GetExecutingAssembly());
});

services
    .AddAllElasticApm()
    .AddLogging(loggerBuilder => configuration.InitializeLogger(
        environment,
        loggerBuilder,
        webAppBuilder.Host,
        HostServiceOptions.AccountingApiName));

var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString();
var isTracingEnabled = services.TryAddTracingSupport(
    configuration,
    environment,
    HostServiceOptions.AccountingApiName,
    serviceVersion);

webHost.AddAndConfigureSentry();

MongoEnumerationSerializerRegistration.RegisterAllBaseEnumerations(typeof(AccountTypes).Assembly);
var app = webAppBuilder.Build();

app.SetupHttpLogging();

app.SetUpBaseApplication(services, environment, configuration);
app.UseAuthorization();
app.MapControllers();

app.MapNotifications();

try
{
    if (isTracingEnabled)
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
        app.MapPrometheusScrapingEndpoint("/metrics");
    }

    await app.RunAsync();
}
catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
{
    app.Logger.LogInformation("Application shutdown requested.");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Fatal error");

    if (!environment.IsIntegrationTesting())
    {
        Environment.Exit(1);
    }
}

static string FormatModelStateError(string key, string errorMessage)
{
    if (string.IsNullOrWhiteSpace(key))
    {
        return errorMessage;
    }

    return $"{key}: {errorMessage}";
}

// To add visibility for integration tests
namespace HomeBudget.Accounting.Api
{
    public partial class Program
    {
        protected Program() { }
    }
}
