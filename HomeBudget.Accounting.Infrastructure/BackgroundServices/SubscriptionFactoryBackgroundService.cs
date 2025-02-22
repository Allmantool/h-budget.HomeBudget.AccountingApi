using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Infrastructure.Services;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class SubscriptionFactoryBackgroundService(
        IOptions<KafkaOptions> options,
        Channel<SubscriptionTopic> topicsChannel,
        IKafkaConsumersFactory kafkaConsumersFactory,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<SubscriptionFactoryBackgroundService> logger)
    : BackgroundService
    {
        private readonly ConcurrentDictionary<string, IKafkaConsumer> _consumers = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumeTasks = Task.Run(() => ConsumeKafkaMessagesLoopAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (topicsChannel.Reader.Completion.IsCompleted)
                    {
                        logger.LogWarning("The topics channel has been completed. No more topics will be processed.");
                        break;
                    }

                    await foreach (var topic in topicsChannel.Reader.ReadAllAsync(stoppingToken))
                    {
                        logger.LogInformation("Received new topic: {Title}", topic.Title);

                        _ = Task.Run(() => ProcessTopicAsync(topic, stoppingToken), stoppingToken);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogInformation("Shutting down {BackgroundService}...", nameof(SubscriptionFactoryBackgroundService));
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Unexpected error in {BackgroundService}. Restarting in {ConsumerDelay} seconds...",
                        nameof(SubscriptionFactoryBackgroundService),
                        options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds);

                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }

            await consumeTasks;
        }

        private async Task ConsumeKafkaMessagesLoopAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Consume loop has been stopped");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_consumers.Count == 0)
                    {
                        logger.LogInformation("No active consumers. Waiting for new topics...");
                        await Task.Delay(TimeSpan.FromMilliseconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                        continue;
                    }

                    var consumersWithSubscriptions = _consumers.Values.Where(c => !c.Subscriptions.IsNullOrEmpty()).ToList();
                    logger.LogInformation("Consuming messages for {Count} active topics...", consumersWithSubscriptions.Count);
                    _ = Task.Run(() => _ = Task.WhenAll(consumersWithSubscriptions.Select(c => c.ConsumeAsync(stoppingToken))), stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error while consuming Kafka messages.");
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumeDelayInMilliseconds), stoppingToken);
                }
            }
        }

        private async Task ProcessTopicAsync(SubscriptionTopic topic, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var sp = scope.ServiceProvider;

                var adminKafkaService = sp.GetRequiredService<IAdminKafkaService>();
                await adminKafkaService.CreateTopicAsync(topic.Title, stoppingToken);

                if (_consumers.ContainsKey(topic.Title))
                {
                    logger.LogWarning("Topic {Title} is already being processed.", topic.Title);
                    return;
                }

                var consumer = kafkaConsumersFactory
                    .WithTopic(topic.Title)
                    .Build(topic.ConsumerType);

                if (_consumers.TryAdd(topic.Title, consumer))
                {
                    consumer.Subscribe(topic.Title);
                    logger.LogInformation("Subscribed to topic {Title}, consumer type {ConsumerType}", topic.Title, topic.ConsumerType);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "{BackgroundService} encountered an error while processing topic {Title}: {ErrorMessage}",
                    nameof(SubscriptionFactoryBackgroundService),
                    topic.Title,
                    ex.Message);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping {BackgroundService}...", nameof(SubscriptionFactoryBackgroundService));

            foreach (var topic in _consumers.Keys)
            {
                if (!_consumers.TryRemove(topic, out var consumer))
                {
                    continue;
                }

                try
                {
                    logger.LogInformation("Unsubscribing from topic {Topic}", topic);
                    consumer.Unsubscribe();
                    consumer.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to stop consumer for topic {Topic}: {ErrorMessage}", topic, ex.Message);
                }
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
