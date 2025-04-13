using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Consumers.Interfaces
{
    public interface IKafkaConsumer : IDisposable
    {
        string ConsumerId { get; }
        IReadOnlyCollection<string> Subscriptions { get; }
        void Subscribe(string topic);
        Task ConsumeAsync(CancellationToken stoppingToken);
        void Unsubscribe();
    }
}
