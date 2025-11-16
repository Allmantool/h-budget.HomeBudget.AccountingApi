using System.Threading.Channels;

using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.BackgroundServices;
using HomeBudget.Accounting.Infrastructure.Providers;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Services;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterInfrastructureDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddSingleton<ITopicManager>(sp =>
                {
                    var pptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
                    var kafkaOptions = pptions.Value;
                    var adminSettings = kafkaOptions.AdminSettings;
                    var consumerSettings = kafkaOptions.ConsumerSettings;

                    var consumerConfig = new ConsumerConfig
                    {
                        BootstrapServers = adminSettings.BootstrapServers,
                        GroupId = consumerSettings.GroupId,
                        EnableAutoCommit = false
                    };

                    var offsetConsumer = new ConsumerBuilder<Ignore, Ignore>(consumerConfig).Build();

                    return new KafkaTopicManager(
                        adminSettings,
                        sp.GetRequiredService<IAdminClient>(),
                        offsetConsumer,
                        sp.GetRequiredService<ILogger<KafkaTopicManager>>());
                })
                .AddSingleton<ITopicProcessor, KafkaTopicProcessor>()
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddSingleton(Channel.CreateUnbounded<AccountRecord>())
                .AddSingleton(sp =>
                {
                    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
                    var adminSettings = kafkaOptions.Value.AdminSettings;

                    var config = new AdminClientConfig
                    {
                        BootstrapServers = adminSettings.BootstrapServers,
                        SocketTimeoutMs = adminSettings.SocketTimeoutMs,
                        MetadataMaxAgeMs = adminSettings.MetadataMaxAgeMs,
                        Debug = adminSettings.Debug,
                        SocketConnectionSetupTimeoutMs = adminSettings.SocketConnectionSetupTimeoutMs,
                        RetryBackoffMs = adminSettings.RetryBackoffMs,
                        CancellationDelayMaxMs = adminSettings.CancellationDelayMaxMs,
                    };

                    return new AdminClientBuilder(config).Build();
                })
                .Configure<HostOptions>(options =>
                {
                    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
                });

                // .RegisterBackgroundServices();
        }

        private static IServiceCollection RegisterBackgroundServices(this IServiceCollection services)
        {
            return services
                .AddHostedService<KafkaConsumerWatchdogWorker>()
                .AddHostedService<KafkaAccountsConsumerWorker>();
        }
    }
}
