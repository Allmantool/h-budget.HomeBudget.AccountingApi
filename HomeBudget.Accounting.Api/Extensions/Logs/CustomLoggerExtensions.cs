using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Exceptions;
using Serilog.Sinks.Elasticsearch;

namespace HomeBudget_Accounting_Api.Extensions.Logs
{
    public static class CustomLoggerExtensions
    {
        public static ILogger InitializeLogger(
            this IConfiguration configuration,
            IWebHostEnvironment environment,
            ConfigureHostBuilder host)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty("Environment", environment)
                .Enrich.WithSpan()
                .WriteTo.Debug()
                .WriteTo.Console()
                .AddElasticSearchSupport(configuration, environment)
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            host.UseSerilog(Log.Logger);

            return Log.Logger;
        }

        private static LoggerConfiguration AddElasticSearchSupport(
            this LoggerConfiguration loggerConfiguration,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            var elasticNodeUrl = (configuration["ElasticConfiguration:Uri"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ?? string.Empty;

            return string.IsNullOrWhiteSpace(elasticNodeUrl)
                ? loggerConfiguration
                : loggerConfiguration.WriteTo.Elasticsearch(ConfigureElasticSink(environment, elasticNodeUrl));
        }

        private static ElasticsearchSinkOptions ConfigureElasticSink(IHostEnvironment environment, string elasticNodeUrl)
        {
            var formattedExecuteAssemblyName = typeof(Program).Assembly.GetName().Name;
            var dateIndexPostfix = DateTime.UtcNow.ToString("MM-yyyy-dd");

            return new ElasticsearchSinkOptions(new Uri(elasticNodeUrl))
            {
                AutoRegisterTemplate = true,
                TypeName = null,
                AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
                BatchAction = ElasticOpType.Create,
                NumberOfReplicas = 1,
                NumberOfShards = 2,
                IndexFormat = $"{formattedExecuteAssemblyName}-{environment.EnvironmentName}-{dateIndexPostfix}".Replace(".", "-").ToLower()
            };
        }
    }
}
