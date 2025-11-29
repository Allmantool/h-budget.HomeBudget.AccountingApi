using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Consumers.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services.Interfaces
{
    public interface IConsumerService
    {
        Task ConsumeKafkaMessagesLoopAsync(IKafkaConsumer consumer, CancellationToken stoppingToken);

        IKafkaConsumer CreateAndSubscribe(SubscriptionTopic topic);
    }
}
