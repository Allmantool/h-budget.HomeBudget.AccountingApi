using System;
using Microsoft.Extensions.DependencyInjection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class ArtifactProtectionDependencyRegistrations
    {
        public static IServiceCollection RegisterRecoveryArtifactPipeline(
            this IServiceCollection services,
            ArtifactProtectionPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(policy);
            services.AddSingleton(policy);
            services.AddScoped<IRecoveryArtifactPipeline, RecoveryArtifactPipeline>();
            return services;
        }
    }
}
