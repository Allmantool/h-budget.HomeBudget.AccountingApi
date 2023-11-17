using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Components.Contractors.Configuration;
using HomeBudget.Components.Operations.Configuration;
using HomeBudget.Components.Categories.Configuration;

namespace HomeBudget.Accounting.Api.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection SetUpDi(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services
                .RegisterContractorsIoCDependency()
                .RegisterOperationsIoCDependency()
                .RegisterCategoriesIoCDependency();
        }
    }
}
