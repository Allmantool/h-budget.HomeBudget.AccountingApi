using System;

namespace HomeBudget.Accounting.Notifications.Models
{
    public record PaymentAccountNotification(string EventId, Guid AccountId, string EventType)
    {
    }
}
