using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core;

namespace HomeBudget.Components.Accounts.Models
{
    internal class AccountOperationEvent : BaseEvent
    {
        public AccountEventTypes EventType { get; init; }
        public PaymentAccount Payload { get; init; }
    }
}
