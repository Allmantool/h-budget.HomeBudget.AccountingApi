using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.BackgroundServices;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterInfrastructureDependencies(this IServiceCollection services)
        {
            return services
                .AddSingleton(Channel.CreateUnbounded<SubscriptionTopic>())
                .AddHostedService<SubscriptionFactoryBackgroundService>()
                .AddScoped<IKafkaConsumersFactory, KafkaConsumersFactory>()
                .AddScoped<IKafkaAdminServiceFactory, KafkaAdminServiceFactory>();
        }
    }
}
