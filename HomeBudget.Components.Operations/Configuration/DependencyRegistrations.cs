﻿using System;

using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Components.Operations.Builders;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Consumers;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
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
                .AddTransient<IKafkaConsumer, PaymentOperationsConsumer>();
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            return services.AddSingleton<IPaymentsHistoryDocumentsClient, PaymentsHistoryDocumentsClient>();
        }

        private static IServiceCollection RegisterEventStoreDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            services.AddSingleton<IEventStoreDbClient<PaymentOperationEvent>, PaymentOperationsEventStoreClient>();

            var serviceProvider = services.BuildServiceProvider();
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<EventStoreDbOptions>>().Value;

            return HostEnvironments.Integration.Equals(webHostEnvironment, StringComparison.OrdinalIgnoreCase)
                ? services
                : services.AddEventStoreClient(
                    databaseOptions.Url.ToString(),
                    _ => EventStoreClientSettings.Create(databaseOptions.Url.ToString()));
        }
    }
}
