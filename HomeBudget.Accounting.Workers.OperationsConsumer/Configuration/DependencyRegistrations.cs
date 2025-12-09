using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Factories;
using HomeBudget.Accounting.Workers.OperationsConsumer.Handlers;
using HomeBudget.Accounting.Workers.OperationsConsumer.Services;
using HomeBudget.Components.Accounts.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Configuration
{
    internal static class DependencyRegistrations
    {
        public static IServiceCollection RegisterWorkerDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

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
            return services.AddHostedService<BatchPaymentEventsProcessorWorker>();
        }
    }
}
