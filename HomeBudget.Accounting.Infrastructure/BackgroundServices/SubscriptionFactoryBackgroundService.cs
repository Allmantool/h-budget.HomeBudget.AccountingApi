using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class SubscriptionFactoryBackgroundService(
        Channel<SubscriptionTopic> topicsChannel,
        IServiceProvider serviceProvider,
        ILogger<SubscriptionFactoryBackgroundService> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var topic in topicsChannel.Reader.ReadAllAsync(stoppingToken))
            {
                using var scope = serviceProvider.CreateScope();
                var sp = scope.ServiceProvider;

                var kafkaAdminServiceFactory = sp.GetRequiredService<IKafkaAdminServiceFactory>();

                var adminKafkaService = kafkaAdminServiceFactory.Build();
                await adminKafkaService.CreateTopicAsync(topic.Title);

                var kafkaConsumersFactory = sp.GetRequiredService<IKafkaConsumersFactory>();

                var consumer = kafkaConsumersFactory
                    .WithServiceProvider(sp)
                    .Build(topic.ConsumerType);

                consumer.Subscribe(topic.Title);

                logger.LogInformation(
                    "Subscribed to topic {Title}, consumer type {ConsumerType}",
                    topic.Title,
                    topic.ConsumerType);

                await consumer.ConsumeAsync(stoppingToken);
            }
        }
    }
}
