using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Components.Accounts.Clients;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Factories;
using HomeBudget.Components.Accounts.Services;
using HomeBudget.Components.Accounts.Services.Interfaces;

namespace HomeBudget.Components.Accounts.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterPaymentAccountsDependencies(
            this IServiceCollection services)
        {
            return services
                .AddScoped<IPaymentAccountFactory, PaymentAccountFactory>()
                .AddScoped<IPaymentAccountService, PaymentAccountService>()
                .RegisterMongoDbClient()
                .AddMediatR(configuration =>
                {
                    configuration.RegisterServicesFromAssembly(typeof(DependencyRegistrations).Assembly);
                });
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services)
        {
            return services.AddSingleton<IPaymentAccountDocumentClient, PaymentAccountDocumentClient>();
        }
    }
}
