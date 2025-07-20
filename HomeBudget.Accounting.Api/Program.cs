﻿using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Extensions;
using HomeBudget.Accounting.Api.Extensions.Logs;
using HomeBudget.Accounting.Api.Extensions.OpenTelemetry;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Components.Operations.MapperProfileConfigurations;

var webAppBuilder = WebApplication.CreateBuilder(args);
var webHost = webAppBuilder.WebHost;
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

services.AddAutoMapper(cfg =>
{
    cfg.AddMaps(typeof(Program).Assembly);
    cfg.AddMaps(PaymentOperationsComponentMappingProfile.GetExecutingAssembly());
});

services.InitializeOpenTelemetry(environment);

services.AddLogging(loggerBuilder => configuration.InitializeLogger(environment, loggerBuilder, webAppBuilder.Host));

webHost.AddAndConfigureSentry();

var webApp = webAppBuilder.Build();

webApp.SetupHttpLogging();

webApp.SetupOpenTelemetry();

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
