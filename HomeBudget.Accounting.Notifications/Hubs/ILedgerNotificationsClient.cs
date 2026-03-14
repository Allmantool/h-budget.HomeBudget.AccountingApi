using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;

namespace HomeBudget.Accounting.Notifications.Hubs
{
    public interface ILedgerNotificationsClient
    {
        Task ReceiveAccountNotification(PaymentAccountNotification notification);
    }
}
