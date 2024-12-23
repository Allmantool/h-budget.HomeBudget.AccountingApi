using System;
using System.Threading.Channels;

using Elastic.Apm.SerilogEnricher;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;

using HomeBudget.Accounting.Api.Constants;

namespace HomeBudget.Accounting.Api.Extensions.Logs
{
    internal static class CustomLoggerExtensions
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
                .Enrich.WithProperty("Host-service", HostServiceOptions.Name)
                .Enrich.WithSpan()
                .WriteTo.Debug()
                .WriteTo.Console()
                .WriteTo.AddAndConfigureSentry(configuration, environment)
                .Enrich.WithElasticApmCorrelationInfo()
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
                : loggerConfiguration.WriteTo.Elasticsearch(ConfigureElasticSink(environment, new Uri(elasticNodeUrl)));
        }

        private static ElasticsearchSinkOptions ConfigureElasticSink(IHostEnvironment environment, Uri elasticNodeUri)
        {
            var formattedExecuteAssemblyName = typeof(Program).Assembly.GetName().Name;
            var dateIndexPostfix = DateTime.UtcNow.ToString("MM-yyyy-dd");

            return new ElasticsearchSinkOptions(new DistributedTransport(new TransportConfiguration(elasticNodeUri)))
            {
                DataStream = new DataStreamName($"{formattedExecuteAssemblyName}-{environment.EnvironmentName}-{dateIndexPostfix}".Replace(".", "-").ToLower()),
                BootstrapMethod = BootstrapMethod.Failure,
                MinimumLevel = LogEventLevel.Debug,
                ConfigureChannel = channelOpts =>
                {
                    channelOpts.BufferOptions = new BufferOptions
                    {
                        BoundedChannelFullMode = BoundedChannelFullMode.DropNewest,
                    };
                }
            };
        }
    }
}
