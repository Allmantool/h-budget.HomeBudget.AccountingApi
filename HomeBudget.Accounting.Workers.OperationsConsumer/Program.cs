using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using HomeBudget.Accounting.Infrastructure.Configuration;
using HomeBudget.Accounting.Workers.OperationsConsumer.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Components.Contractors.Configuration;
using HomeBudget.Components.Operations.Configuration;

namespace HomeBudget.Accounting.Workers.OperationsConsumer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var host = CreateHost(args);
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex}");
                Environment.Exit(1);
            }
        }

        public static IHost CreateHost(
            string[] args = null,
            Action<IServiceCollection> configureServices = null,
            string environmentName = null)
        {
            var builder = Host.CreateApplicationBuilder(args ?? Array.Empty<string>());

            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["DOTNET_ENVIRONMENT"] = environmentName,
                    ["ASPNETCORE_ENVIRONMENT"] = environmentName
                });
                builder.Environment.EnvironmentName = environmentName;
            }

            var services = builder.Services;
            var configuration = builder.Configuration;
            var environment = builder.Environment;

            services
                .RegisterWorkerDependencies(configuration)
                .RegisterInfrastructureDependencies(configuration)
                .RegisterContractorsDependencies()
                .RegisterOperationsDependencies(environment.EnvironmentName)
                .RegisterCategoriesDependencies()
                .AddHostedService<KafkaPaymentsConsumerWorker>();

            configureServices?.Invoke(services);

            return builder.Build();
        }
    }
}
