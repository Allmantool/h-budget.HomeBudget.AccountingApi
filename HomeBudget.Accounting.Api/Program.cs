using System.Collections.Generic;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Api.Configuration;
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

webAppBuilder.Services.AddControllers(o =>
{
    o.Conventions.Add(new SwaggerControllerDocConvention());
});

services.SetUpDi(configuration, environment);

webAppBuilder.Services.AddEndpointsApiExplorer();
webAppBuilder.Services.AddSwaggerGen();

services.SetupSwaggerGen();

services.AddHeaderPropagation(options =>
{
    options.Headers.Add(HttpHeaderKeys.HostService, "HomeBudget-Accounting-Api");
    options.Headers.Add(HttpHeaderKeys.CorrelationId);
});

services.AddAutoMapper(new List<Assembly>
{
    typeof(Program).Assembly,
    PaymentOperationsComponentMappingProfile.GetExecutingAssembly(),
});

configuration.InitializeLogger(environment, webAppBuilder.Host);

var webApp = webAppBuilder.Build();

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
