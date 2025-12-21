using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer
{
    internal class EventStoreDbPaymentsConsumerWorker(
        ILogger<EventStoreDbPaymentsConsumerWorker> logger,
        IOptions<KafkaOptions> options,
        PaymentOperationsEventStoreSubscriptionReadClient client)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await client.CreatePersistentSubscriptionAsync(stoppingToken);
            await client.SubscribeAsync(stoppingToken);
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
