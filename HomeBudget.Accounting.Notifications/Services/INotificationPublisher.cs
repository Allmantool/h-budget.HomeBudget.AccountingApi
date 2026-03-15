using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Services
{
    public interface INotificationPublisher
    {
        Task PublishAsync(PaymentAccountNotification evt);
    }
}
