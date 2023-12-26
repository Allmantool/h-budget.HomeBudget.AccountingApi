using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using EventStore.Client;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterOperationsIoCDependency(this IServiceCollection services, string webHostEnvironment)
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
                .RegisterEventStoreDbClient(webHostEnvironment);
        }

        private static IServiceCollection RegisterOperationsClients(this IServiceCollection services)
        {
            return services
                .AddSingleton<IKafkaClientHandler, PaymentOperationsClientHandlerHandler>()
                .AddSingleton<IKafkaDependentProducer<string, string>, PaymentOperationsDependentProducer<string, string>>();
        }

        private static IServiceCollection RegisterEventStoreDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            services.AddSingleton<IEventStoreDbClient<PaymentOperationEvent>, PaymentOperationsEventStoreClient>();

            var serviceProvider = services.BuildServiceProvider();
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<EventStoreDbOptions>>().Value;

            if (HostEnvironments.Integration.Equals(webHostEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                return services;
            }

            return services.AddEventStoreClient(databaseOptions.Url, _ => EventStoreClientSettings.Create(databaseOptions.Url));
        }
    }
}
