using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterOperationsIoCDependency(this IServiceCollection services)
        {
            return services
                .AddScoped<IOperationFactory, OperationFactory>()
                .AddScoped<IPaymentOperationsService, PaymentOperationsService>()
                .AddScoped<IPaymentOperationsHistoryService, PaymentOperationsHistoryService>()
                .AddMediatR(configuration =>
                {
                    configuration.RegisterServicesFromAssembly(typeof(DependencyRegistrations).Assembly);
                })
                .RegisterOperationsClients()
                .RegisterEventStoreDbClient();
        }

        private static IServiceCollection RegisterOperationsClients(this IServiceCollection services)
        {
            return services
                .AddSingleton<IKafkaClientHandler, PaymentOperationsClientHandlerHandler>()
                .AddSingleton<IKafkaDependentProducer<string, string>, PaymentOperationsDependentProducer<string, string>>();
        }

        private static IServiceCollection RegisterEventStoreDbClient(this IServiceCollection services)
        {
            var serviceProvider = services.BuildServiceProvider();
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<EventStoreDbOptions>>().Value;

            var dbConnection = new Uri(databaseOptions.Url);

            return services.AddEventStoreClient(dbConnection)
                .AddSingleton<IEventStoreDbClient, PaymentOperationsEventStoreClient>();
        }
    }
}
