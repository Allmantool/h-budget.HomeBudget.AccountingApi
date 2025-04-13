﻿using System.Diagnostics;

using Elastic.Apm.SerilogEnricher;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;

using HomeBudget.Accounting.Api.Constants;

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
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty(LoggerTags.Environment, environment.EnvironmentName)
                .Enrich.WithProperty(LoggerTags.HostService, HostServiceOptions.Name)
                .Enrich.WithProperty(LoggerTags.ApplicationName, environment.ApplicationName)
                .Enrich.WithProperty(LoggerTags.TraceId, () => Activity.Current?.TraceId.ToString())
                .Enrich.WithProperty(LoggerTags.SpanId, () => Activity.Current?.SpanId.ToString())
                .Enrich.WithSpan()
                .WriteTo.Debug()
                .WriteTo.Console(new RenderedCompactJsonFormatter(), restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.AddAndConfigureSentry(configuration, environment)
                .Enrich.WithElasticApmCorrelationInfo()
                .TryAddSeqSupport(configuration)
                .TryAddElasticSearchSupport(configuration, environment)
                .ReadFrom.Configuration(configuration)
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
