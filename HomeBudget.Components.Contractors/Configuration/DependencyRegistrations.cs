using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Components.Contractors.Clients;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Contractors.Factories;
using HomeBudget.Accounting.Domain.Factories;

namespace HomeBudget.Components.Contractors.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterContractorsDependencies(
            this IServiceCollection services)
        {
            return services
                .AddScoped<IContractorFactory, ContractorFactory>()
                .RegisterMongoDbClient();
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services)
        {
            return services.AddSingleton<IContractorDocumentsClient, ContractorDocumentsClient>();
        }
    }
}
