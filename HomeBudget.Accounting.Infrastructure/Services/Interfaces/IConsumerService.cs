using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services.Interfaces
{
    internal interface IConsumerService
    {
        Task ConsumeKafkaMessagesLoopAsync(CancellationToken stoppingToken);

        IKafkaConsumer CreateAndSubscribe(SubscriptionTopic topic);
    }
}
