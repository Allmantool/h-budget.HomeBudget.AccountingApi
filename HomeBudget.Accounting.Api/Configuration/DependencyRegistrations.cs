using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Configuration;
using HomeBudget.Components.Accounts.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Components.Contractors.Configuration;
using HomeBudget.Components.Operations.Configuration;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection SetUpDi(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment)
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            return services
                .SetUpConfigurationOptions(configuration)
                .RegisterPaymentAccountsDependencies()
                .RegisterContractorsDependencies()
                .RegisterOperationsDependencies(webHostEnvironment.EnvironmentName)
                .RegisterCategoriesDependencies()
                .RegisterInfrastructureDependencies();
        }

        private static IServiceCollection SetUpConfigurationOptions(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services
                .Configure<KafkaOptions>(configuration.GetSection(ConfigurationSectionKeys.KafkaOptions))
                .Configure<MongoDbOptions>(configuration.GetSection(ConfigurationSectionKeys.MongoDbOptions))
                .Configure<EventStoreDbOptions>(configuration.GetSection(ConfigurationSectionKeys.EventStoreDb));
        }
    }
}
