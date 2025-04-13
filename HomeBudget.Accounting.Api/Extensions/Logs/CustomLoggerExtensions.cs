using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.Extensions.Logs
{
    internal static class CustomLoggerExtensions
    {
        public static Logger InitializeLogger(
            this IConfiguration configuration,
            IWebHostEnvironment environment,
            ILoggingBuilder loggingBuilder,
            ConfigureHostBuilder host)
        {
            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty(LoggerTags.Environment, environment)
                .Enrich.WithProperty(LoggerTags.HostService, HostServiceOptions.Name)
                .Enrich.WithSpan()
                .WriteTo.Debug()
                .WriteTo.Console()
                .WriteTo.AddAndConfigureSentry(configuration, environment)
                .Enrich.WithElasticApmCorrelationInfo()
                .TryAddSeqSupport(configuration)
                .TryAddElasticSearchSupport(configuration, environment)
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(logger);

            host.UseSerilog(logger);

            Log.Logger = logger;

            return logger;
        }

        private static LoggerConfiguration TryAddSeqSupport(this LoggerConfiguration loggerConfiguration, IConfiguration configuration)
        {
            try
            {
                var seqOptions = configuration.GetSection(ConfigurationSectionKeys.SeqOptions)?.Get<SeqOptions>();

                if (!seqOptions.IsEnabled)
                {
                    return loggerConfiguration;
                }

                var seqUrl = seqOptions.Uri?.ToString() ?? Environment.GetEnvironmentVariable("SEQ_URL");

                loggerConfiguration.WriteTo.Seq(seqUrl);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine($"Failed to configure Seq sink: {ex}");
            }

            return loggerConfiguration;
        }

        private static LoggerConfiguration TryAddElasticSearchSupport(
            this LoggerConfiguration loggerConfiguration,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            try
            {
                var elasticOptions = configuration.GetSection(ConfigurationSectionKeys.ElasticSearchOptions)?.Get<ElasticSearchOptions>();

                if (!elasticOptions.IsEnabled)
                {
                    return loggerConfiguration;
                }

                var elasticNodeUrl = (elasticOptions.Uri?.ToString() ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ?? string.Empty;

                return string.IsNullOrWhiteSpace(elasticNodeUrl)
                    ? loggerConfiguration
                    : loggerConfiguration
                        .Enrich.WithElasticApmCorrelationInfo()
                        .WriteTo.Elasticsearch(
                            new List<Uri>
                            {
                                new(elasticNodeUrl)
                            },
                            opt => opt.ConfigureElasticSink(environment));
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine($"Elasticsearch sink initialization failed: {ex}");
            }

            return loggerConfiguration;
        }

        private static void ConfigureElasticSink(this ElasticsearchSinkOptions options, IHostEnvironment environment)
        {
            var formattedExecuteAssemblyName = typeof(Program).Assembly.GetName().Name;
            var dateIndexPostfix = DateTime.UtcNow.ToString(DateTimeFormats.ElasticSearch);
            var streamName = $"{formattedExecuteAssemblyName}-{environment.EnvironmentName}-{dateIndexPostfix}".Replace(".", "-").ToLower();

            options.DataStream = new DataStreamName(streamName);
            options.BootstrapMethod = BootstrapMethod.Failure;
            options.MinimumLevel = LogEventLevel.Debug;
            options.ConfigureChannel = channelOpts =>
            {
                channelOpts.BufferOptions = new BufferOptions
                {
                    BoundedChannelFullMode = BoundedChannelFullMode.DropNewest,
                };
            };
        }
    }
}
