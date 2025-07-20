using System.Collections.Generic;
using System.Threading;

using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Infrastructure.Services.Interfaces
{
    public interface ITopicProcessor
    {
        IEnumerable<SubscriptionTopic> GetTopicsWithLag(CancellationToken token);
    }
}
