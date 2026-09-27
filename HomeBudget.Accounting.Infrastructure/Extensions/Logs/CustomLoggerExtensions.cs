using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Enrichers.Span;
using Serilog.Exceptions;
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
                .Enrich.WithProperty("service.name", hostServiceName)
                .Enrich.WithProperty("service.namespace", "HomeBudget")
                .Enrich.WithProperty("service.instance.id", System.Environment.MachineName)
                .Enrich.WithProperty("deployment.environment", environment.EnvironmentName)
                .WriteTo.Debug()
                .WriteTo.AddAndConfigureSentry(configuration, environment)
                .TryAddSeqSupport(configuration)
                .TryAddElasticSearchSupport(configuration, environment, hostServiceName);

            var logsEndpoint = configuration.GetSection("ObservabilityOptions:LogsEndpoint")?.Value;
            if (!string.IsNullOrWhiteSpace(logsEndpoint))
            {
                var serviceVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                loggerConfiguration = loggerConfiguration.WriteTo.OpenTelemetry(o =>
                {
                    o.Endpoint = logsEndpoint;
                    o.Protocol = OtlpProtocol.Grpc;
                    o.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = hostServiceName,
                        ["service.namespace"] = "HomeBudget",
                        ["service.version"] = serviceVersion,
                        ["service.instance.id"] = System.Environment.MachineName,
                        ["deployment.environment"] = environment.EnvironmentName,
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

