using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Models
{
    internal class PaymentOperationEvent
    {
        public Guid PaymentOperationId { get; set; }

        public long OperationUnixTime { get; set; }

        public PaymentOperation Payload { get; set; }

        public EventTypes EventType { get; set; }
    }
}
