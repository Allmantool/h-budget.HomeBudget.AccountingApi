using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Consumers.Interfaces
{
    public interface IKafkaConsumer
    {
        void Subscribe(string topic);
        Task ConsumeAsync(CancellationToken stoppingToken);
    }
}
