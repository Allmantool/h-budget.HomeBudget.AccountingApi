using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;

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
        IOptions<EventStoreDbOptions> options,
        IEventStoreDbSubscriptionReadClient<PaymentOperationEvent> client)
        : BackgroundService
    {
        private PersistentSubscription _subscription;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await client.CreatePersistentSubscriptionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _subscription?.Dispose();
                    _subscription = null;

                    _subscription = await client.SubscribeAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.SubscriptionFailed(ex);
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.RetryInSeconds), stoppingToken);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _subscription?.Dispose();
                _subscription = null;
            }
            catch (Exception ex)
            {
                logger.FailedToDisposeConsumer(ex);
            }

            return base.StopAsync(cancellationToken);
        }
    }
}
