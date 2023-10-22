using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Components.Contractors.Configuration;

namespace HomeBudget.Accounting.Api.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection SetUpDi(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services.RegisterContractorsIoCDependency();
        }
    }
}
