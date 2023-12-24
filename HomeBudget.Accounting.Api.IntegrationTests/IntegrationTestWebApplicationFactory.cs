using System;
using System.IO;

using EventStore.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    public class IntegrationTestWebApplicationFactory<TStartup>
        (Func<string> webHostInitializationCallback) : WebApplicationFactory<TStartup>
        where TStartup : class
    {
        private string _eventDbConnection;

        internal IConfiguration Configuration { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, conf) =>
            {
                conf.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{HostEnvironments.Integration}.json"));
                conf.AddEnvironmentVariables();

                Configuration = conf.Build();

                _eventDbConnection = webHostInitializationCallback?.Invoke();
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddEventStoreClient(_eventDbConnection, (_) => EventStoreClientSettings.Create(_eventDbConnection));
            });

            base.ConfigureWebHost(builder);
        }
    }
}
