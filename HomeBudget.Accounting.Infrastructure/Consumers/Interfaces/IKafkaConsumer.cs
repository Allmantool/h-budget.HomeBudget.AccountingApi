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
        void Assign(string topic);
        void Unassign();
        void Subscribe(string topic);
        void UnSubscribe();
        Task ConsumeAsync(CancellationToken stoppingToken);
    }
}
