using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using HomeBudget.Accounting.Notifications.Models;
using HomeBudget.Accounting.Notifications.Services;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Notifications
{
    internal sealed class AccountingApiNotificationPublisher(HttpClient httpClient)
        : INotificationPublisher
    {
        public async Task PublishAsync(PaymentAccountNotification evt)
        {
            ArgumentNullException.ThrowIfNull(evt);

            using var response = await httpClient.PostAsJsonAsync(
                "notifications/account/publish",
                evt);

            response.EnsureSuccessStatusCode();
        }
    }
}
