using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaTopicCreationListenerBackgroundService(
        ILogger<KafkaTopicCreationListenerBackgroundService> logger,
        IOptions<KafkaOptions> options,
        Channel<SubscriptionTopic> topicsChannel,
        ITopicProcessor topicService)
    : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
                        _ = Task.Run(
                            () => topicService.EnsureProcessingAsync(topic, stoppingToken),
                            stoppingToken);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogError(ex, "Shutting down {Service}...", nameof(KafkaTopicCreationListenerBackgroundService));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                    ex,
                    "Unexpected error in {Service}. Restarting in {Delay} seconds...",
                    nameof(KafkaTopicCreationListenerBackgroundService),
                    options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds);

                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }
        }
    }
}
