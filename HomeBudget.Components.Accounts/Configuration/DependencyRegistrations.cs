using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Components.Accounts.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterPaymentAccountsIoCDependency(
            this IServiceCollection services)
        {
            return services
                .AddMediatR(configuration =>
                {
                    configuration.RegisterServicesFromAssembly(typeof(DependencyRegistrations).Assembly);
                });
        }
    }
}
