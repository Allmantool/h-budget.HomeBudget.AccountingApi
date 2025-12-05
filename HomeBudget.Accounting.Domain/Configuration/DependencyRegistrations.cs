using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Domain.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection SetUpConfigurationOptions(
           this IServiceCollection services,
           IConfiguration configuration)
        {
            if (configuration is null)
            {
                return services;
            }

            return services
                .Configure<KafkaOptions>(configuration.GetSection(ConfigurationSectionKeys.KafkaOptions))
                .Configure<MongoDbOptions>(configuration.GetSection(ConfigurationSectionKeys.MongoDbOptions))
                .Configure<EventStoreDbOptions>(configuration.GetSection(ConfigurationSectionKeys.EventStoreDb));
        }
    }
}
