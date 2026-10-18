using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
using HomeBudget.Accounting.Infrastructure.DataProtection.Retention;
using HomeBudget.Accounting.Infrastructure.DataProtection.Triggers;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class DataProtectionDependencyRegistrations
    {
        public static IServiceCollection RegisterDataProtectionControlPlane(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services
                .AddOptions<DataProtectionWorkerOptions>()
                .Bind(configuration.GetSection(DataProtectionWorkerOptions.SectionName))
                .Validate(
                    options => options.PollIntervalMilliseconds is >= 10 and <= 60000,
                    "Data-protection polling interval must be between 10 and 60000 milliseconds.")
                .Validate(
                    options => options.LeaseDurationSeconds is >= 5 and <= 3600,
                    "Data-protection lease duration must be between 5 and 3600 seconds.")
                .ValidateOnStart();
            services
                .AddOptions<LedgerGenerationOptions>()
                .Bind(configuration.GetSection(LedgerGenerationOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.EnvironmentId), "Ledger generation environment is required.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.LedgerId), "Ledger id is required.")
                .ValidateOnStart();

            services.AddScoped<IDataProtectionOperationStore>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
                return new SqlDataProtectionOperationStore(options.ConnectionString);
            });
            services.AddScoped<IDataProtectionOperationExecutionControl>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
                return new SqlDataProtectionOperationExecutionControl(options.ConnectionString);
            });
            services.AddScoped<IDataProtectionPlanStore>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
                return new SqlDataProtectionPlanStore(options.ConnectionString);
            });
            services.TryAddSingleton(TimeProvider.System);
            services.AddScoped<IDataProtectionAdministrationService, DataProtectionAdministrationService>();
            services.TryAddScoped<IDataProtectionOperationExecutor, UnsupportedDataProtectionOperationExecutor>();
            services.AddSingleton<IActiveLedgerGenerationResolver>(serviceProvider =>
            {
                var database = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                var generation = serviceProvider.GetRequiredService<IOptions<LedgerGenerationOptions>>().Value;
                return new SqlActiveLedgerGenerationResolver(database.ConnectionString, generation);
            });
            services.AddSingleton<IAccountDeliveryDispositionStore>(serviceProvider =>
            {
                var database = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                return new SqlAccountDeliveryDispositionStore(database.ConnectionString);
            });
            services.AddSingleton<SqlCaptureCoordinationStore>(serviceProvider =>
            {
                var database = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                return new SqlCaptureCoordinationStore(database.ConnectionString);
            });
            services.AddSingleton<ICaptureCoordinationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqlCaptureCoordinationStore>());
            services.AddSingleton<IMaintenanceAdmissionFence>(serviceProvider =>
                serviceProvider.GetRequiredService<SqlCaptureCoordinationStore>());
            services.AddSingleton<CommonCutoffCalculator>();
            services.AddScoped<DataProtectionOperationProcessor>();

            return services;
        }

        public static IServiceCollection RegisterDataProtectionWorker(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.AddHostedService<DataProtectionOperationWorker>();
        }

        public static IServiceCollection RegisterRecoveryRetentionAndTriggers(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            services.AddOptions<RecoveryTriggerOptions>()
                .Bind(configuration.GetSection(RecoveryTriggerOptions.SectionName))
                .Validate(options => options.ScheduledCadence > TimeSpan.Zero, "Backup cadence must be positive.")
                .Validate(options => options.MaximumCoalescingDelay > TimeSpan.Zero, "Coalescing delay must be positive.")
                .Validate(options => options.RecordThreshold > 0, "Record threshold must be positive.")
                .Validate(options => options.ByteThreshold > 0, "Byte threshold must be positive.")
                .Validate(options => options.SchedulerPollInterval > TimeSpan.Zero, "Scheduler polling must be positive.")
                .ValidateOnStart();
            services.TryAddSingleton(new RecoveryRetentionOptions());
            services.TryAddSingleton<RecoveryRetentionPolicy>();
            services.TryAddSingleton<RecoveryTriggerPolicy>(serviceProvider =>
                new RecoveryTriggerPolicy(serviceProvider.GetRequiredService<IOptions<RecoveryTriggerOptions>>().Value));
            services.TryAddSingleton<IRecoveryTriggerStateStore>(serviceProvider =>
            {
                var database = serviceProvider.GetRequiredService<IOptions<DatabaseConnectionOptions>>().Value;
                return new SqlRecoveryTriggerStateStore(database.ConnectionString);
            });
            services.TryAddSingleton<RecoveryTriggerCoordinator>();
            services.TryAddScoped<MandatoryPreOperationBackupGate>();
            return services;
        }

        public static IServiceCollection RegisterRecoveryTriggerScheduler(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.AddHostedService<RecoveryTriggerScheduler>();
        }
    }
}
