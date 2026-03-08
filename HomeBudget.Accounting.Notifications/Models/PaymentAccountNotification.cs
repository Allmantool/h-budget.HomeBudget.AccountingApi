using System;

namespace HomeBudget.Accounting.Notifications.Models
{
    public sealed record PaymentAccountNotification(
        string EventId,
        string EventType,
        Guid AccountId
    );
}
