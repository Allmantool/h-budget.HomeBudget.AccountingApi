using Elastic.Apm.SerilogEnricher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Extensions.Logs;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Extensions
{
    internal static class CustomLoggerExtensions
    {
        public static Logger InitializeLogger(
            this IConfiguration configuration,
            IHostEnvironment environment,
            ILoggingBuilder loggingBuilder,
            IHostApplicationBuilder hostApplicationBuilder,
            string hostServiceName)
        {
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .Enrich.WithProcessName()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty(LoggerTags.Environment, environment.EnvironmentName)
                .Enrich.WithProperty(LoggerTags.HostService, hostServiceName)
                .Enrich.WithProperty(LoggerTags.ApplicationName, environment.ApplicationName)
                .Enrich.WithSpan()
                .Enrich.WithActivityId()
                .Enrich.WithActivityTags()
                .WriteTo.Debug()
                .WriteTo.Console(
                    new RenderedCompactJsonFormatter(),
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.AddAndConfigureSentry(configuration, environment)
                .WriteTo.OpenTelemetry(o =>
                {
                    o.Endpoint = configuration.GetSection("ObservabilityOptions:LogsEndpoint")?.Value;
                    o.Protocol = OtlpProtocol.Grpc;
                })
                .Enrich.WithElasticApmCorrelationInfo()
                .TryAddSeqSupport(configuration)
                .TryAddElasticSearchSupport(configuration, environment, typeof(Program).Assembly.GetName().Name)
                .CreateLogger();

            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(logger);

            loggingBuilder.AddOpenTelemetry(options =>
            {
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.IncludeFormattedMessage = true;
                options.AddOtlpExporter();
            });

            // SelfLog.Enable(msg => File.AppendAllText("serilog-accounting-worker-selflog.txt",
            Log.Logger = logger;

            return logger;
        }
    }
}
