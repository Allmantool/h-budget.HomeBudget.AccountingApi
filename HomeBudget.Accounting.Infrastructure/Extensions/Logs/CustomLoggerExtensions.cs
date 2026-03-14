using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

using HomeBudget.Accounting.Infrastructure.Constants;

namespace HomeBudget.Accounting.Infrastructure.Extensions.Logs
{
    public static class CustomLoggerExtensions
    {
        public static Logger InitializeLogger(
            this IConfiguration configuration,
            IWebHostEnvironment environment,
            ILoggingBuilder loggingBuilder,
            ConfigureHostBuilder host,
            string hostServiceName)
        {
            var loggerConfiguration = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .Enrich.WithProcessName()
                .Enrich.WithExceptionDetails()
                .Enrich.WithSpan()
                .Enrich.With<ActivityEnricher>()
                .Enrich.WithActivityId()
                .Enrich.WithActivityTags()
                .Enrich.WithProperty(LoggerTags.Environment, environment.EnvironmentName)
                .Enrich.WithProperty(LoggerTags.HostService, hostServiceName)
                .Enrich.WithProperty(LoggerTags.ApplicationName, environment.ApplicationName)
                .WriteTo.Debug()
                .WriteTo.Console(
                    new RenderedCompactJsonFormatter(),
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.AddAndConfigureSentry(configuration, environment)
                .TryAddSeqSupport(configuration)
                .TryAddElasticSearchSupport(configuration, environment, hostServiceName);

            var logsEndpoint = configuration.GetSection("ObservabilityOptions:LogsEndpoint")?.Value;
            if (!string.IsNullOrWhiteSpace(logsEndpoint))
            {
                var serviceVersion = typeof(CustomLoggerExtensions).Assembly.GetName().Version?.ToString() ?? "unknown";
                loggerConfiguration = loggerConfiguration.WriteTo.OpenTelemetry(o =>
                {
                    o.Endpoint = logsEndpoint;
                    o.Protocol = OtlpProtocol.Grpc;
                    o.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = hostServiceName,
                        ["service.version"] = serviceVersion,
                        [LoggerTags.Environment] = environment.EnvironmentName,
                        [LoggerTags.HostService] = hostServiceName,
                        [LoggerTags.ApplicationName] = environment.ApplicationName,
                    };
                });
            }

            var logger = loggerConfiguration.CreateLogger();

            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(logger);

            host.UseSerilog(logger);

            Log.Logger = logger;

            return logger;
        }

        public static WebApplication SetupHttpLogging(this WebApplication app)
        {
            app.UseSerilogRequestLogging(options =>
            {
                options.EnrichDiagnosticContext = LogEnricher.HttpRequestEnricher;
            });

            return app;
        }
    }
}

