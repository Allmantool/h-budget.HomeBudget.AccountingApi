using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services.Interfaces
{
    public interface ITopicProcessor
    {
        IEnumerable<SubscriptionTopic> GetTopicsWithLag(CancellationToken token);
        Task EnsureProcessingAsync(SubscriptionTopic topic, CancellationToken token);
    }
}
