using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer
{
    internal class EventStoreDbPaymentsConsumerWorker(
        ILogger<EventStoreDbPaymentsConsumerWorker> logger,
        IOptions<KafkaOptions> options,
        IEventStoreDbSubscriptionReadClient<PaymentOperationEvent> client)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await client.CreatePersistentSubscriptionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                   await client.SubscribeAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // graceful shutdown
                }
                catch (Exception ex)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
            }
            catch (Exception ex)
            {
                logger.FailedToDisposeConsumer(ex);
            }

            return base.StopAsync(cancellationToken);
        }
    }
}
