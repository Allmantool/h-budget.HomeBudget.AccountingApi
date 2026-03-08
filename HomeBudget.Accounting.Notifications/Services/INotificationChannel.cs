using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Services
{
    public interface INotificationChannel
    {
        ValueTask PublishAsync(PaymentAccountNotification notification);

        IAsyncEnumerable<PaymentAccountNotification> ReadAllAsync(CancellationToken ct);
    }
}
