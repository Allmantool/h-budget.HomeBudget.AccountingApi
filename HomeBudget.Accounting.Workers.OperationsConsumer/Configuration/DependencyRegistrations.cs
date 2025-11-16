using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Factories;
using HomeBudget.Accounting.Workers.OperationsConsumer.Services;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Configuration
{
    internal static class DependencyRegistrations
    {
        public static IServiceCollection RegisterWorkerDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .Configure<KafkaOptions>(configuration.GetSection(ConfigurationSectionKeys.KafkaOptions))
                .AddSingleton<IKafkaConsumersFactory, KafkaConsumersFactory>()
                .AddSingleton<IConsumerService, KafkaConsumerService>();
        }
    }
}
