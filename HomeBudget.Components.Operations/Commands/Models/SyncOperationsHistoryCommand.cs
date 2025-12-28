using System;
using System.Collections.Generic;

using MediatR;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class SyncOperationsHistoryCommand(
        Guid paymentAccountId,
        IEnumerable<PaymentOperationEvent> events)
        : IRequest<Result<decimal>>, ICorrelatedCommand
    {
        public string CorrelationId { get; set; }
        public Guid PaymentAccountId { get; } = paymentAccountId;
        public IEnumerable<PaymentOperationEvent> Events { get; } = events;
    }
}
