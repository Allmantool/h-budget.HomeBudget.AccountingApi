using System;
using System.Threading.Channels;

using EventStore.Client;
using EventStoreDbClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.BackgroundServices;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Components.Operations.Builders;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Consumers;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterOperationsDependencies(this IServiceCollection services, string webHostEnvironment)
        {
            return services
                .AddScoped<IFinancialTransactionFactory, FinancialTransactionFactory>()
                .AddScoped<ICrossAccountsTransferBuilder, CrossAccountsTransferBuilder>()
                .AddScoped<IPaymentOperationsService, PaymentOperationsService>()
                .AddScoped<IPaymentOperationsHistoryService, PaymentOperationsHistoryService>()
                .AddScoped<ICrossAccountsTransferService, CrossAccountsTransferService>()
                .AddScoped<IFireAndForgetHandler<IKafkaProducer<string, string>>, FireAndForgetKafkaProducerHandler>()
                .RegisterCommandHandlers()
                .RegisterOperationsClients()
                .RegisterEventStoreDbClient(webHostEnvironment)
                .RegisterBackgroundServices()
                .RegisterMongoDbClient(webHostEnvironment);
        }

        private static IServiceCollection RegisterCommandHandlers(this IServiceCollection services)
        {
            return services
                .AddMediatR(configuration =>
                {
                    configuration.RegisterServicesFromAssembly(typeof(DependencyRegistrations).Assembly);
                });
        }

        private static IServiceCollection RegisterOperationsClients(this IServiceCollection services)
        {
            return services
                .AddSingleton<IKafkaClientHandler, PaymentOperationsClientHandler>()
                .AddSingleton<IKafkaProducer<string, string>, PaymentOperationsProducer>()
                .AddSingleton<IPaymentOperationsDeliveryHandler, PaymentOperationsDeliveryHandler>()
                .AddTransient<BaseKafkaConsumer<string, string>, PaymentOperationsConsumer>();
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            return services.AddSingleton<IPaymentsHistoryDocumentsClient, PaymentsHistoryDocumentsClient>();
        }

        private static IServiceCollection RegisterBackgroundServices(this IServiceCollection services)
        {
            return services
                .AddSingleton(Channel.CreateUnbounded<PaymentOperationEvent>())
                .AddHostedService<PaymentOperationsBatchProcessorBackgroundService>();
        }

        private static IServiceCollection RegisterEventStoreDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            services.AddSingleton<IEventStoreDbClient<PaymentOperationEvent>, PaymentOperationsEventStoreClient>();

            var serviceProvider = services.BuildServiceProvider();
            var eventStoreDbOptions = serviceProvider.GetRequiredService<IOptions<EventStoreDbOptions>>().Value;
            var eventStoreUrl = eventStoreDbOptions.Url.ToString();

            return HostEnvironments.Integration.Equals(webHostEnvironment, StringComparison.OrdinalIgnoreCase)
                ? services
                : services.AddEventStoreClient(
                    eventStoreUrl,
                    settings =>
                    {
                        settings = EventStoreClientSettings.Create(eventStoreUrl);
                        settings.OperationOptions = new EventStoreClientOperationOptions
                        {
                            ThrowOnAppendFailure = true,
                        };
                        settings.DefaultDeadline = TimeSpan.FromSeconds(eventStoreDbOptions.TimeoutInSeconds * (eventStoreDbOptions.RetryAttempts + 1));
                        settings.ConnectivitySettings = new EventStoreClientConnectivitySettings
                        {
                            KeepAliveInterval = TimeSpan.FromSeconds(eventStoreDbOptions.KeepAliveInterval),
                            GossipTimeout = TimeSpan.FromSeconds(eventStoreDbOptions.GossipTimeout),
                            DiscoveryInterval = TimeSpan.FromSeconds(eventStoreDbOptions.DiscoveryInterval),
                            MaxDiscoverAttempts = eventStoreDbOptions.MaxDiscoverAttempts
                        };
                    });
        }
    }
}
