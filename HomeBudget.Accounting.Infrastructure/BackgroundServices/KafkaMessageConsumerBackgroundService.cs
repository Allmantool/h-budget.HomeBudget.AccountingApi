using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Services.Interfaces;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaMessageConsumerBackgroundService(
        IConsumerService kafkaConsumerService,
        ILogger<KafkaMessageConsumerBackgroundService> logger)
    : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() => kafkaConsumerService.ConsumeKafkaMessagesLoopAsync(stoppingToken), stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping {BackgroundService}...", nameof(KafkaMessageConsumerBackgroundService));

            foreach (var topic in ConsumersStore.Consumers.Keys)
            {
                if (!ConsumersStore.Consumers.TryRemove(topic, out var consumer))
                {
                    continue;
                }

                try
                {
                    logger.LogInformation("Unsubscribing from topic {Topic}", topic);

                    // consumer.Unassign();
                    consumer.UnSubscribe();
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
