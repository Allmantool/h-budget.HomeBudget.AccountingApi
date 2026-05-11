using System;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Components.Operations.Builders;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Consumers;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Options;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Components.Operations.Validators;
using HomeBudget.Core.Options;
using HomeBudget.Core.Validation;

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
                .AddScoped<IOutboxPaymentStatusService, OutboxPaymentStatusService>()
                .AddScoped<IPaymentMessageInboxService, PaymentMessageInboxService>()
                .AddScoped<IRequestValidator<Commands.Models.AddPaymentOperationCommand>, AddPaymentOperationCommandValidator>()
                .AddScoped<IRequestValidator<Commands.Models.UpdatePaymentOperationCommand>, UpdatePaymentOperationCommandValidator>()
                .AddScoped<IRequestValidator<Commands.Models.RemovePaymentOperationCommand>, RemovePaymentOperationCommandValidator>()
                .AddScoped<IRequestValidator<Commands.Models.ApplyTransferCommand>, ApplyTransferCommandValidator>()
                .AddScoped<IRequestValidator<Commands.Models.UpdateTransferCommand>, UpdateTransferCommandValidator>()
                .AddScoped<IRequestValidator<Commands.Models.RemoveTransferCommand>, RemoveTransferCommandValidator>()
                .AddScoped<PaymentOutboxPublisher>()
                .RegisterOperationsClients()
                .RegisterEventStoreDbClient(webHostEnvironment)
                .RegisterMongoDbClient(webHostEnvironment);
        }

        public static IServiceCollection RegisterPaymentOutboxPublisher(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services
                .Configure<PaymentOutboxPublisherOptions>(
                    configuration.GetSection(PaymentOutboxPublisherOptions.SectionName))
                .Configure<PaymentInboxOptions>(
                    configuration.GetSection(PaymentInboxOptions.SectionName))
                .AddHostedService<PaymentOutboxMetricsWorker>()
                .AddHostedService<PaymentOutboxPublisherWorker>();
        }

        private static IServiceCollection RegisterOperationsClients(this IServiceCollection services)
        {
            return services
                .AddSingleton<IKafkaClientHandler, PaymentOperationsClientHandler>()
                .AddSingleton<IKafkaProducer<string, string>, PaymentOperationsProducer>()
                .AddSingleton<BaseKafkaConsumer<string, string>, PaymentOperationsConsumer>();
        }

        private static IServiceCollection RegisterMongoDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            return services.AddSingleton<IPaymentsHistoryDocumentsClient, PaymentsHistoryDocumentsClient>();
        }

        private static IServiceCollection RegisterEventStoreDbClient(this IServiceCollection services, string webHostEnvironment)
        {
            services.AddSingleton<IEventStoreDbWriteClient<PaymentOperationEvent>, PaymentOperationsEventStoreWriteClient>();

            if (HostEnvironments.Integration.Equals(webHostEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                return services;
            }

            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<EventStoreDbOptions>>().Value;
                var settings = EventStoreClientSettings.Create(options.Url.OriginalString);

                settings.OperationOptions = new EventStoreClientOperationOptions
                {
                    ThrowOnAppendFailure = true,
                };

                settings.DefaultDeadline = TimeSpan.FromSeconds(options.TimeoutInSeconds * (options.RetryAttempts + 1));
                settings.ConnectivitySettings.KeepAliveInterval = TimeSpan.FromSeconds(options.KeepAliveInterval);
                settings.ConnectivitySettings.GossipTimeout = TimeSpan.FromSeconds(options.GossipTimeout);
                settings.ConnectivitySettings.DiscoveryInterval = TimeSpan.FromSeconds(options.DiscoveryInterval);
                settings.ConnectivitySettings.MaxDiscoverAttempts = options.MaxDiscoverAttempts;

                return settings;
            });

            services.AddSingleton(sp => new EventStoreClient(sp.GetRequiredService<EventStoreClientSettings>()));
            services.AddSingleton(sp => new EventStorePersistentSubscriptionsClient(sp.GetRequiredService<EventStoreClientSettings>()));

            return services;
        }
    }
}
