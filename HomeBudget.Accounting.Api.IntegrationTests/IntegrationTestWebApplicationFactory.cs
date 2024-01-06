using System;
using System.IO;

using EventStore.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    public class IntegrationTestWebApplicationFactory<TStartup>
        (Func<TestContainersConnections> webHostInitializationCallback) : WebApplicationFactory<TStartup>
        where TStartup : class
    {
        private TestContainersConnections _containersConnections;

        internal IConfiguration Configuration { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, conf) =>
            {
                conf.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{HostEnvironments.Integration}.json"));
                conf.AddEnvironmentVariables();

                Configuration = conf.Build();

                _containersConnections = webHostInitializationCallback?.Invoke();
            });

            builder.ConfigureTestServices(services =>
            {
                var kafkaOptions = new KafkaOptions
                {
                    ProducerSettings = new ProducerSettings
                    {
                        BootstrapServers = _containersConnections.KafkaContainer
                    }
                };

                var mongoDbOptions = new PaymentsHistoryDbOptions
                {
                    ConnectionString = _containersConnections.MongoDbContainer
                };

                services.AddOptions<KafkaOptions>().Configure(options => options.ProducerSettings = kafkaOptions.ProducerSettings);
                services.AddOptions<PaymentsHistoryDbOptions>().Configure(options =>
                {
                    options.ConnectionString = mongoDbOptions.ConnectionString;
                    options.DatabaseName = "payments-history";
                });

                services.AddEventStoreClient(
                    _containersConnections.EventSourceDbContainer,
                    (_) => EventStoreClientSettings.Create(_containersConnections.EventSourceDbContainer));
            });

            base.ConfigureWebHost(builder);
        }
    }
}
