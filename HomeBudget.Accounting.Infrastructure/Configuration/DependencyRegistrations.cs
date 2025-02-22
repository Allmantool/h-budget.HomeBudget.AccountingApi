using System.Threading.Channels;

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.BackgroundServices;
using HomeBudget.Accounting.Infrastructure.Services;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterInfrastructureDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddScoped<IAdminKafkaService>(sp =>
                {
                    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
                    var adminSettings = kafkaOptions.Value.AdminSettings;

                    return new AdminKafkaService(
                        adminSettings,
                        sp.GetRequiredService<IAdminClient>(),
                        sp.GetRequiredService<ILogger<AdminKafkaService>>());
                })
                .AddSingleton(Channel.CreateUnbounded<SubscriptionTopic>())
                .AddSingleton(sp =>
                {
                    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
                    var adminSettings = kafkaOptions.Value.AdminSettings;

                    var config = new AdminClientConfig
                    {
                        BootstrapServers = adminSettings.BootstrapServers,
                        SocketTimeoutMs = adminSettings.SocketTimeoutMs,
                        Debug = adminSettings.Debug
                    };

                    return new AdminClientBuilder(config).Build();
                })
                .AddHostedService<SubscriptionFactoryBackgroundService>()
                .AddSingleton<IKafkaConsumersFactory, KafkaConsumersFactory>();
        }
    }
}
