using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Components.Accounts.Clients;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Consumers;
using HomeBudget.Components.Accounts.Factories;
using HomeBudget.Components.Accounts.Handlers;
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
                .AddScoped<IPaymentAccountProducerService, PaymentAccountProducerService>()
                .RegisterMongoDbClient()
                .RegisterAccountsClients();
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services)
        {
            return services.AddSingleton<IPaymentAccountDocumentClient, PaymentAccountDocumentClient>();
        }

        private static IServiceCollection RegisterAccountsClients(this IServiceCollection services)
        {
            return services
                .AddSingleton<IKafkaProducer<string, string>, PaymentAccountProducer>()
                .AddSingleton<IAccountOperationsHandler, AccountOperationsHandler>()
                .AddSingleton<BaseKafkaConsumer<string, string>, AccountOperationsConsumer>();
        }
    }
}
