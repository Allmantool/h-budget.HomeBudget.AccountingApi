using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Services
{
    public interface INotificationChannel
    {
        Task PublishAsync(PaymentAccountNotification evt);

        IAsyncEnumerable<PaymentAccountNotification> ReadAsync(
            string lastEventId = null,
            [EnumeratorCancellation] CancellationToken ct = default);
    }
}
