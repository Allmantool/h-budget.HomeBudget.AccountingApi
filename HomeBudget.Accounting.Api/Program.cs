using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using HomeBudget_Accounting_Api.Extensions;

var webAppBuilder = WebApplication.CreateBuilder(args);
var services = webAppBuilder.Services;
var environment = webAppBuilder.Environment;

webAppBuilder.Services.AddControllers();

webAppBuilder.Services.AddEndpointsApiExplorer();
webAppBuilder.Services.AddSwaggerGen();

services.SetupSwaggerGen();

var app = webAppBuilder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.SetUpSwaggerUi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();

// To add visibility for integration tests
namespace HomeBudget.Accounting.Api
{
    public partial class Program
    {
        protected Program() { }
    }
}
