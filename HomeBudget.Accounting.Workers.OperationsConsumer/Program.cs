using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Serilog;

using HomeBudget.Accounting.Domain.Configuration;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Accounting.Infrastructure.Configuration;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Extensions;
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
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.UseUrls("http://127.0.0.1:0");

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
            var applicationName = environment.ApplicationName;
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
                .AddHostedService<KafkaPaymentsConsumerWorker>()
                .AddHostedService<EventStoreDbPaymentsConsumerWorker>();

            services
                .AddHealthChecks()
                .AddCheck("heartbeat", () => HealthCheckResult.Healthy());

            var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString();
            var isTracingEnabled = services.TryAddTracingSupport(
                configuration,
                environment,
                HostServiceOptions.AccountConsumerWorkerName,
                serviceVersion);

            services.AddLogging(loggerBuilder => configuration.InitializeLogger(environment, loggerBuilder, builder, HostServiceOptions.AccountConsumerWorkerName));

            services.AddEndpointsApiExplorer();

            builder.AddAndConfigureSentry(configuration);

            configureServices?.Invoke(services);

            MongoEnumerationSerializerRegistration.RegisterAllBaseEnumerations(typeof(CategoryTypes).Assembly);

            var app = builder.Build();

            if (isTracingEnabled)
            {
                app.UseOpenTelemetryPrometheusScrapingEndpoint();

                app.MapHealthChecks("/health");
                app.MapPrometheusScrapingEndpoint("/metrics");
            }

            return app;
        }
    }
}
