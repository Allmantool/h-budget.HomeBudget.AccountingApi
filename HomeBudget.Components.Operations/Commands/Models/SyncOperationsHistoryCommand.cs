using System;
using System.Collections.Generic;

using MediatR;

using HomeBudget.Core.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class SyncOperationsHistoryCommand(Guid paymentAccountId, IEnumerable<PaymentOperationEvent> eventsForAccount)
        : IRequest<Result<decimal>>
    {
        public Guid PaymentAccountId { get; } = paymentAccountId;
        public IEnumerable<PaymentOperationEvent> EventsForAccount { get; } = eventsForAccount;
    }
}
