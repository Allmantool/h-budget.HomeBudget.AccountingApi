using System;
using System.Reflection;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.Extensibility;
using Sentry.Infrastructure;

using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.Extensions
{
    public static class SentryExtensions
    {
        public static IHostBuilder AddAndConfigureSentry(this IHostBuilder hostBuilder)
        {
            return AddAndConfigureSentry(hostBuilder, new SentryConfigurationOptions());
        }

        public static IHostBuilder AddAndConfigureSentry(this IHostBuilder hostBuilder, SentryConfigurationOptions sentryOptions)
        {
            return hostBuilder.ConfigureServices((hostBuilderContext, services) =>
            {
                if (!TryIfSentryConfigurationValid(sentryOptions, hostBuilderContext.Configuration, out var verifiedOptions))
                {
                    return;
                }

                var environment = hostBuilderContext.HostingEnvironment;
                services.AddLogging(logging =>
                {
                    logging.AddSentry(sentryLoggingOptions =>
                    {
                        var version = Assembly.GetExecutingAssembly().GetName().Version;

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
                        sentryLoggingOptions.TracesSampleRate = environment.IsDevelopment() ? 1.0 : 0.3;
                        sentryLoggingOptions.IsGlobalModeEnabled = true;
                        sentryLoggingOptions.AttachStacktrace = true;
                        sentryLoggingOptions.SendDefaultPii = environment.IsDevelopment();
                        sentryLoggingOptions.MinimumBreadcrumbLevel = environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information;
                        sentryLoggingOptions.MinimumEventLevel = LogLevel.Warning;
                        sentryLoggingOptions.DiagnosticLevel = SentryLevel.Error;
                    });
                });
            });
        }

        public static IWebHostBuilder AddAndConfigureSentry(this IWebHostBuilder webHostBuilder)
        {
            return AddAndConfigureSentry(webHostBuilder, new SentryConfigurationOptions());
        }

        public static IWebHostBuilder AddAndConfigureSentry(this IWebHostBuilder webHostBuilder, SentryConfigurationOptions sentryOptions)
        {
            return webHostBuilder.UseSentry((webHostBuilderContext, sentryAspNetCoreOptions) =>
            {
                if (!TryIfSentryConfigurationValid(sentryOptions, webHostBuilderContext.Configuration, out var verifiedOptions))
                {
                    return;
                }

                var environment = webHostBuilderContext.HostingEnvironment;
                var version = Assembly.GetExecutingAssembly().GetName().Version;

                if (version != null)
                {
                    sentryAspNetCoreOptions.Release = version.ToString();
                }

                if (environment.IsDevelopment())
                {
                    sentryAspNetCoreOptions.Debug = true;
                    sentryAspNetCoreOptions.DiagnosticLogger = new TraceDiagnosticLogger(SentryLevel.Debug);
                }

                sentryAspNetCoreOptions.Environment = environment.EnvironmentName;
                sentryAspNetCoreOptions.Dsn = verifiedOptions.Dns;
                sentryAspNetCoreOptions.TracesSampleRate = environment.IsDevelopment() ? 1.0 : 0.3;
                sentryAspNetCoreOptions.IsGlobalModeEnabled = true;
                sentryAspNetCoreOptions.AttachStacktrace = true;
                sentryAspNetCoreOptions.SendDefaultPii = environment.IsDevelopment(); // Disable sending PII for security (e.g., user emails)
                sentryAspNetCoreOptions.MaxRequestBodySize = environment.IsDevelopment() ? RequestSize.Always : RequestSize.Small;
                sentryAspNetCoreOptions.MinimumBreadcrumbLevel = environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information;
                sentryAspNetCoreOptions.MinimumEventLevel = LogLevel.Warning;
                sentryAspNetCoreOptions.DiagnosticLevel = SentryLevel.Error;
            });
        }

        private static bool TryIfSentryConfigurationValid(
            SentryConfigurationOptions sentryOptions,
            IConfiguration configuration,
            out SentryConfigurationOptions verifiedOptions)
        {
            verifiedOptions = null;
            var sentryDns = sentryOptions.Dns ?? configuration["Sentry:Dsn"] ?? throw new InvalidOperationException("Sentry DSN is missing.");
            var isOptionsValid = !string.IsNullOrWhiteSpace(sentryDns);

            if (isOptionsValid)
            {
                verifiedOptions = new SentryConfigurationOptions
                {
                    Dns = sentryDns
                };
            }

            return isOptionsValid;
        }
    }
}
