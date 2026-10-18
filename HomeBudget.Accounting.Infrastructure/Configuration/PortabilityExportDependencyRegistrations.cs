using System;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class PortabilityExportDependencyRegistrations
    {
        public static IServiceCollection RegisterPortabilityExporter<TReader>(
            this IServiceCollection services,
            Action<PortabilityExportOptions> configure)
            where TReader : class, IPortabilityExportReader
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);
            var options = new PortabilityExportOptions();
            configure(options);
            options.Validate();

            return services
                .AddSingleton(options)
                .AddScoped<IPortabilityExportReader, TReader>()
                .AddScoped<PortabilityExportService>();
        }
    }
}
