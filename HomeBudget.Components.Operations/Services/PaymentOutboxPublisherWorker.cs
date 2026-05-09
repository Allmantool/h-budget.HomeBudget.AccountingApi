using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Components.Operations.Options;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOutboxPublisherWorker(
        ILogger<PaymentOutboxPublisherWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<PaymentOutboxPublisherOptions> options)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var publisherOptions = options.Value;
            if (!publisherOptions.IsEnabled)
            {
                logger.LogInformation("Payment outbox publisher is disabled.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var publisher = scope.ServiceProvider.GetRequiredService<PaymentOutboxPublisher>();
                    await publisher.PublishRetryableRowsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Payment outbox publisher iteration failed.");
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(publisherOptions.PollIntervalMilliseconds),
                    stoppingToken);
            }
        }
    }
}
