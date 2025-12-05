using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Factories;

namespace HomeBudget.Components.Categories.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterCategoriesDependencies(
            this IServiceCollection services)
        {
            return services
                .AddScoped<ICategoryFactory, CategoryFactory>()
                .RegisterMongoDbClient();
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services)
        {
            return services.AddSingleton<ICategoryDocumentsClient, CategoryDocumentsClient>();
        }
    }
}
