using Microsoft.AspNetCore.SignalR;

namespace HomeBudget.Accounting.Notifications.Hubs
{
    public sealed class LedgerNotificationsHub : Hub<ILedgerNotificationsClient>
    {
        public const string Route = "/notifications/account-hub";
    }
}
