using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

using HomeBudget_Accounting_Api.Extensions;

var webAppBuilder = WebApplication.CreateBuilder(args);
var services = webAppBuilder.Services;
var environment = webAppBuilder.Environment;
var configuration = webAppBuilder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true)
    .Build();

webAppBuilder.Services.AddControllers();

webAppBuilder.Services.AddEndpointsApiExplorer();
webAppBuilder.Services.AddSwaggerGen();

services.SetupSwaggerGen();
var webApp = webAppBuilder.Build();

webApp.SetUpBaseApplication(services, environment, configuration);

// Configure the HTTP request pipeline.
if (webApp.Environment.IsDevelopment())
{
}

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
