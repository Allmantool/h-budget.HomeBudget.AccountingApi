using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Consumers.Interfaces
{
    public interface IKafkaConsumer : IDisposable
    {
        void Subscribe(string topic);
        Task ConsumeAsync(CancellationToken stoppingToken);
        void Unsubscribe();
    }
}
