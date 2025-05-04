using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Services.Interfaces
{
    public interface ITopicManager
    {
        Task CreateAsync(string topic, CancellationToken token);
        Task DeleteAsync(string topic);
        IReadOnlyCollection<string> GetAll();
        long GetTopicLag(string topic);
        Task<bool> HasActiveConsumerAsync(string topic, string consumerGroupId);
    }
}
