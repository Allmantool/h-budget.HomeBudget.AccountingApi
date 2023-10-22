using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Contractors.Factories;

namespace HomeBudget.Components.Contractors.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterContractorsIoCDependency(
            this IServiceCollection services)
        {
            return services
                .AddScoped<IContractorFactory, ContractorFactory>();
        }
    }
}
