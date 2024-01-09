using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Configuration;
using HomeBudget.Components.Categories.Configuration;
using HomeBudget.Components.Contractors.Configuration;
using HomeBudget.Components.Operations.Configuration;

namespace HomeBudget.Accounting.Api.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection SetUpDi(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment)
        {
            return services
                .SetUpConfigurationOptions(configuration)
                .RegisterPaymentAccountsIoCDependency()
                .RegisterContractorsIoCDependency()
                .RegisterOperationsIoCDependency(webHostEnvironment.EnvironmentName)
                .RegisterCategoriesIoCDependency();
        }

        public static IServiceCollection SetUpConfigurationOptions(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services
                .Configure<KafkaOptions>(configuration.GetSection(ConfigurationSectionKeys.KafkaOptions))
                .Configure<MongoDbOptions>(configuration.GetSection(ConfigurationSectionKeys.PaymentsHistoryDbOptions))
                .Configure<EventStoreDbOptions>(configuration.GetSection(ConfigurationSectionKeys.EventStoreDb));
        }
    }
}
