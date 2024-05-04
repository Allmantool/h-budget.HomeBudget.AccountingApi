using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Components.Transfers.Services;
using HomeBudget.Components.Transfers.Services.Interfaces;

namespace HomeBudget.Components.Transfers.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterTransfersDependencies(this IServiceCollection services)
        {
            return services
                .AddScoped<ICrossAccountsTransferService, CrossAccountsTransferService>();
        }
    }
}
