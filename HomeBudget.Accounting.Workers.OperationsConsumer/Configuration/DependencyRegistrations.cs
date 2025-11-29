using System.Threading.Channels;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Factories;
using HomeBudget.Accounting.Workers.OperationsConsumer.Handlers;
using HomeBudget.Accounting.Workers.OperationsConsumer.Services;
using HomeBudget.Components.Accounts.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Configuration
{
    internal static class DependencyRegistrations
    {
        public static IServiceCollection RegisterWorkerDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .RegisterBackgroundServices()
                .RegisterPaymentAccountsDependencies()
                .RegisterCategoriesDependencies()
                .Configure<KafkaOptions>(configuration.GetSection(ConfigurationSectionKeys.KafkaOptions))
                .AddSingleton<IKafkaConsumersFactory, KafkaConsumersFactory>()
                .AddSingleton<IConsumerService, KafkaConsumerService>()
                .AddSingleton<IPaymentOperationsDeliveryHandler, PaymentOperationsDeliveryHandler>();
        }

        private static IServiceCollection RegisterBackgroundServices(this IServiceCollection services)
        {
            return services
                .AddSingleton(Channel.CreateUnbounded<PaymentOperationEvent>())
                .AddHostedService<BatchPaymentEventsProcessorWorker>();
        }
    }
}
