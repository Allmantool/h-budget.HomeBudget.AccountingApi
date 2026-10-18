using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class PortabilityImportDependencyRegistrations
    {
        public static IServiceCollection RegisterPortabilityImport<TTargetProbe>(
            this IServiceCollection services,
            Action<PortabilityImportPreflightOptions> configure)
            where TTargetProbe : class, IPortabilityImportTargetProbe
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);
            var options = new PortabilityImportPreflightOptions();
            configure(options);
            options.Validate();

            services
                .AddSingleton(options)
                .AddScoped<IPortabilityImportTargetProbe, TTargetProbe>()
                .AddScoped<PortabilityImportPreflightService>()
                .AddScoped<IPortabilityImportJournalStore>(provider =>
                {
                    var database = provider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                    return new SqlPortabilityImportJournalStore(database.ConnectionString);
                });
            return services;
        }

        public static IServiceCollection RegisterPortabilityImportExecution<TTargetProbe, TApplicationAdapter>(
            this IServiceCollection services,
            Action<PortabilityImportPreflightOptions> configurePreflight,
            Action<PortabilityImportExecutionOptions> configureExecution)
            where TTargetProbe : class, IPortabilityImportTargetProbe
            where TApplicationAdapter : class, IPortabilityImportApplicationAdapter
        {
            ArgumentNullException.ThrowIfNull(configureExecution);
            services.RegisterPortabilityImport<TTargetProbe>(configurePreflight);
            var options = new PortabilityImportExecutionOptions();
            configureExecution(options);
            options.Validate();

            services
                .AddSingleton(options)
                .AddScoped<IPortabilityImportApplicationAdapter, TApplicationAdapter>()
                .AddScoped<PortabilityImportExecutionService>();
            return services;
        }

        public static IServiceCollection RegisterPortabilityImportReconciliation<TTargetProbe, TReadbackAdapter>(
            this IServiceCollection services,
            Action<PortabilityImportPreflightOptions> configurePreflight,
            Action<PortabilityImportReconciliationOptions> configureReconciliation)
            where TTargetProbe : class, IPortabilityImportTargetProbe
            where TReadbackAdapter : class, IPortabilityImportReadbackAdapter
        {
            ArgumentNullException.ThrowIfNull(configureReconciliation);
            services.RegisterPortabilityImport<TTargetProbe>(configurePreflight);
            var options = new PortabilityImportReconciliationOptions();
            configureReconciliation(options);
            options.Validate();

            services
                .AddSingleton(options)
                .AddScoped<IPortabilityImportReadbackAdapter, TReadbackAdapter>()
                .AddScoped<PortabilityImportReconciliationService>();
            return services;
        }

        public static IServiceCollection RegisterFamilyProPortabilityCompatibility<TTargetProbe>(
            this IServiceCollection services,
            Action<PortabilityImportPreflightOptions> configurePreflight)
            where TTargetProbe : class, IPortabilityImportTargetProbe
        {
            services.RegisterPortabilityImport<TTargetProbe>(configurePreflight);
            services.AddScoped<FamilyProPortabilityCompatibilityService>();
            return services;
        }
    }
}
