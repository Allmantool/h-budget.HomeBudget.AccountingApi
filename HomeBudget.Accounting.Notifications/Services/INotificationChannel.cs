using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Services
{
    public interface INotificationChannel : INotificationPublisher
    {
        IAsyncEnumerable<PaymentAccountNotification> ReadAsync(
            string lastEventId = null,
            CancellationToken ct = default);
    }
}
