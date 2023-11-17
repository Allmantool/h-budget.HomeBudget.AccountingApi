using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Categories.Factories;

namespace HomeBudget.Components.Categories.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterCategoriesIoCDependency(
            this IServiceCollection services)
        {
            return services
                .AddScoped<ICategoryFactory, CategoryFactory>();
        }
    }
}
