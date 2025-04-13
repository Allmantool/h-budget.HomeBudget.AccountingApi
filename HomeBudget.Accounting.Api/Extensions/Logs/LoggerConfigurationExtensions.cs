using System;
using System.Collections.Generic;
using System.Threading.Channels;

using Elastic.Apm.SerilogEnricher;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.Extensions.Logs
{
    internal static class LoggerConfigurationExtensions
    {
        public static LoggerConfiguration TryAddSeqSupport(this LoggerConfiguration loggerConfiguration, IConfiguration configuration)
        {
            try
            {
                var seqOptions = configuration.GetSection(ConfigurationSectionKeys.SeqOptions)?.Get<SeqOptions>();

                if (!seqOptions.IsEnabled)
                {
                    return loggerConfiguration;
                }

                var seqUrl = seqOptions.Uri?.ToString() ?? Environment.GetEnvironmentVariable("SEQ_URL");

                loggerConfiguration.WriteTo.Seq(seqUrl, restrictedToMinimumLevel: LogEventLevel.Information);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine($"Failed to configure Seq sink: {ex}");
            }

            return loggerConfiguration;
        }

        public static LoggerConfiguration TryAddElasticSearchSupport(
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
