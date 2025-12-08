using System;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.Infrastructure;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Extensions
{
    internal static class SentryExtensions
    {
        public static LoggerConfiguration AddAndConfigureSentry(
            this LoggerSinkConfiguration loggerConfiguration,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            return AddAndConfigureSentry(
                loggerConfiguration,
                configuration,
                environment,
                new SentryConfigurationOptions());
        }

        public static LoggerConfiguration AddAndConfigureSentry(
            this LoggerSinkConfiguration loggerConfiguration,
            IConfiguration configuration,
            IHostEnvironment environment,
            SentryConfigurationOptions sentryOptions)
        {
            return loggerConfiguration.Sentry(sentrySerilogOptions =>
            {
                if (!TryIfSentryConfigurationValid(sentryOptions, configuration, out var verifiedOptions))
                {
                    sentrySerilogOptions.Dsn = string.Empty;
                    return;
                }

                var version = typeof(SentryExtensions).Assembly.GetName().Version;

                if (version != null)
                {
                    sentrySerilogOptions.Release = version.ToString();
                }

                if (environment.IsUnderDevelopment())
                {
                    sentrySerilogOptions.Debug = true;
                    sentrySerilogOptions.DiagnosticLogger = new TraceDiagnosticLogger(SentryLevel.Debug);
                }

                sentrySerilogOptions.Environment = environment.EnvironmentName;
                sentrySerilogOptions.Dsn = verifiedOptions.Dns;
                sentrySerilogOptions.TracesSampleRate = environment.IsUnderDevelopment()
                    ? SentryBaseOptions.TracesSampleRateForDevelopment
                    : SentryBaseOptions.TracesSampleRateForProduction;
                sentrySerilogOptions.IsGlobalModeEnabled = true;
                sentrySerilogOptions.AttachStacktrace = true;
                sentrySerilogOptions.SendDefaultPii = environment.IsUnderDevelopment(); // Disable sending PII for security (e.g., user emails)
                sentrySerilogOptions.MinimumBreadcrumbLevel = environment.IsUnderDevelopment() ? LogEventLevel.Debug : LogEventLevel.Information;
                sentrySerilogOptions.MinimumEventLevel = LogEventLevel.Warning;
                sentrySerilogOptions.DiagnosticLevel = SentryLevel.Error;
            });
        }

        public static void AddAndConfigureSentry(
            this IHostApplicationBuilder hostBuilder,
            IConfiguration configuration,
            SentryConfigurationOptions sentryOptions = null)
        {
            sentryOptions ??= new SentryConfigurationOptions();

            if (!TryIfSentryConfigurationValid(sentryOptions, configuration, out var verifiedOptions))
            {
                return;
            }

            var environment = hostBuilder.Environment;

            hostBuilder.Logging.AddSentry(sentryLoggingOptions =>
            {
                var version = typeof(SentryExtensions).Assembly.GetName().Version;
                if (version != null)
                {
                    sentryLoggingOptions.Release = version.ToString();
                }

                if (environment.IsDevelopment())
                {
                    sentryLoggingOptions.Debug = true;
                    sentryLoggingOptions.DiagnosticLogger = new TraceDiagnosticLogger(SentryLevel.Debug);
                }

                sentryLoggingOptions.Environment = environment.EnvironmentName;
                sentryLoggingOptions.Dsn = verifiedOptions.Dns;
                sentryLoggingOptions.TracesSampleRate = environment.IsDevelopment()
                    ? SentryBaseOptions.TracesSampleRateForDevelopment
                    : SentryBaseOptions.TracesSampleRateForProduction;
                sentryLoggingOptions.IsGlobalModeEnabled = true;
                sentryLoggingOptions.AttachStacktrace = true;
                sentryLoggingOptions.SendDefaultPii = environment.IsDevelopment();
                sentryLoggingOptions.MinimumBreadcrumbLevel = environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information;
                sentryLoggingOptions.MinimumEventLevel = LogLevel.Warning;
                sentryLoggingOptions.DiagnosticLevel = SentryLevel.Error;
            });
        }

        private static bool TryIfSentryConfigurationValid(
            SentryConfigurationOptions sentryOptions,
            IConfiguration configuration,
            out SentryConfigurationOptions verifiedOptions)
        {
            verifiedOptions = null;

            var sentryDsn = sentryOptions.Dns ?? configuration[SentryBaseOptions.UriConfigurationKey];
            if (string.IsNullOrWhiteSpace(sentryDsn))
            {
                return false;
            }

            if (!Uri.TryCreate(sentryDsn, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            verifiedOptions = new SentryConfigurationOptions
            {
                Dns = sentryDsn
            };

            return true;
        }
    }
}
