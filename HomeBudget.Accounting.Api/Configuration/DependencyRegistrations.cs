using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;

using HomeBudget.Accounting.Infrastructure.Configuration;
using HomeBudget.Components.Accounts.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Components.Contractors.Configuration;
using HomeBudget.Components.Operations.Configuration;
using HomeBudget.Accounting.Domain.Configuration;

namespace HomeBudget.Accounting.Api.Configuration
{
    internal static class DependencyRegistrations
    {
        public static IServiceCollection SetUpDi(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment)
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

            return services
                .SetUpConfigurationOptions(configuration)
                .RegisterPaymentAccountsDependencies()
                .RegisterContractorsDependencies()
                .RegisterOperationsDependencies(webHostEnvironment.EnvironmentName)
                .RegisterCategoriesDependencies()
                .RegisterInfrastructureDependencies(configuration);
        }
    }
}
