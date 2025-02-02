using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
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
        private readonly ConcurrentDictionary<string, IKafkaConsumer> _consumers = new();
        private const int MaxDegreeOfConcurrency = 10;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var semaphore = new SemaphoreSlim(MaxDegreeOfConcurrency);

            await foreach (var topic in topicsChannel.Reader.ReadAllAsync(stoppingToken))
            {
                await semaphore.WaitAsync(stoppingToken);

                _ = ProcessTopicAsync(topic, semaphore, stoppingToken);
            }
        }

        private async Task ProcessTopicAsync(
            SubscriptionTopic topic,
            SemaphoreSlim semaphore,
            CancellationToken stoppingToken)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var sp = scope.ServiceProvider;

                var kafkaAdminServiceFactory = sp.GetRequiredService<IKafkaAdminServiceFactory>();
                var adminKafkaService = kafkaAdminServiceFactory.Build();
                await adminKafkaService.CreateTopicAsync(topic.Title, stoppingToken);

                var kafkaConsumersFactory = sp.GetRequiredService<IKafkaConsumersFactory>();
                var consumer = kafkaConsumersFactory
                    .WithServiceProvider(sp)
                    .Build(topic.ConsumerType);

                consumer.Subscribe(topic.Title);

                _consumers.TryAdd(topic.Title, consumer);

                logger.LogInformation(
                    "Subscribed to topic {Title}, consumer type {ConsumerType}",
                    topic.Title,
                    topic.ConsumerType);

                _ = consumer.ConsumeAsync(stoppingToken).ContinueWith(
                    _ =>
                    {
                        _consumers.TryRemove(topic.Title, out var _);
                        semaphore.Release();
                    }, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "{BackgroundService} encountered an error while processing topic {Title}: {ErrorMessage}",
                    nameof(SubscriptionFactoryBackgroundService),
                    topic.Title,
                    ex.Message);

                semaphore.Release();
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var consumer in _consumers.Values)
            {
                try
                {
                    // await consumer.StopAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to stop consumer for topic: {ErrorMessage}",
                        ex.Message);
                }
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
