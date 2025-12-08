using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

using HomeBudget.Accounting.Domain.Configuration;
using HomeBudget.Accounting.Infrastructure.Configuration;
using HomeBudget.Accounting.Workers.OperationsConsumer.Configuration;
using HomeBudget.Accounting.Workers.OperationsConsumer.Extensions;
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
                Log.Logger.Error($"Fatal error: {ex}");
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
            var environment = builder.Environment;
            var configuration = builder.Configuration
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true)
                .Build();

            services
                .SetUpConfigurationOptions(configuration)
                .RegisterWorkerDependencies(configuration)
                .RegisterInfrastructureDependencies(configuration)
                .RegisterContractorsDependencies()
                .RegisterOperationsDependencies(environment.EnvironmentName)
                .RegisterCategoriesDependencies()
                .AddHostedService<KafkaPaymentsConsumerWorker>();

            services.AddLogging(loggerBuilder => configuration.InitializeLogger(environment, loggerBuilder, builder));

            builder.AddAndConfigureSentry(configuration);

            configureServices?.Invoke(services);

            return builder.Build();
        }
    }
}
