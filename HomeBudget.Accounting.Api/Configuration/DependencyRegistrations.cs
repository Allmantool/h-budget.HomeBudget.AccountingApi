using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Components.Accounts.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Components.Contractors.Configuration;
using HomeBudget.Components.Operations.Configuration;

namespace HomeBudget.Accounting.Api.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection SetUpDi(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services
                .RegisterPaymentAccountsIoCDependency()
                .RegisterContractorsIoCDependency()
                .RegisterOperationsIoCDependency()
                .RegisterCategoriesIoCDependency();
        }
    }
}
