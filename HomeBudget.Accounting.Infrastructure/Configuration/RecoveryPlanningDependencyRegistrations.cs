using System;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.DataProtection.Recovery;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class RecoveryPlanningDependencyRegistrations
    {
        public static IServiceCollection RegisterRecoveryPlanning<TEvidenceSource>(this IServiceCollection services)
            where TEvidenceSource : class, IRecoveryPlanningEvidenceSource
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddScoped<IRecoveryPlanningEvidenceSource, TEvidenceSource>();
            services.AddScoped<RecoveryPlanSelector>();
            services.AddScoped<RecoveryPlanningCoordinator>();
            return services;
        }

        public static IServiceCollection RegisterRecoveryExecution<TJournalStore, TActiveGenerationGuard>(
            this IServiceCollection services)
            where TJournalStore : class, IRecoveryExecutionJournalStore
            where TActiveGenerationGuard : class, IRecoveryActiveGenerationGuard
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddScoped<IRecoveryExecutionJournalStore, TJournalStore>();
            services.AddScoped<IRecoveryActiveGenerationGuard, TActiveGenerationGuard>();
            services.AddScoped<RecoveryExecutionCoordinator>();
            return services;
        }

        public static IServiceCollection AddRecoveryRestoreStageAdapter<TAdapter>(this IServiceCollection services)
            where TAdapter : class, IRecoveryRestoreStageAdapter
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddScoped<IRecoveryRestoreStageAdapter, TAdapter>();
            return services;
        }

        public static IServiceCollection RegisterRecoveryCompletion<TReplay, TRebuild, TReconciliation, TActivator>(
            this IServiceCollection services)
            where TReplay : class, IRecoveryPendingReplayAdapter
            where TRebuild : class, IRecoveryProjectionRebuildAdapter
            where TReconciliation : class, IRecoveryApiReconciliationAdapter
            where TActivator : class, IRecoveryGenerationActivator
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddScoped<IRecoveryPendingReplayAdapter, TReplay>();
            services.AddScoped<IRecoveryProjectionRebuildAdapter, TRebuild>();
            services.AddScoped<IRecoveryApiReconciliationAdapter, TReconciliation>();
            services.AddScoped<IRecoveryGenerationActivator, TActivator>();
            services.AddScoped<RecoveryCompletionCoordinator>();
            return services;
        }
    }
}
