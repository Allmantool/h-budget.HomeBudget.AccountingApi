using System;
using System.Collections.Generic;

using MediatR;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public sealed class SyncOperationsHistoryCommand(
        Guid paymentAccountId,
        IEnumerable<PaymentOperationEvent> events,
        ProjectionCheckpoint checkpoint = null)
        : IRequest<Result<decimal>>, ICorrelatedCommand
    {
        public string CorrelationId { get; set; }
        public Guid PaymentAccountId { get; } = paymentAccountId;
        public IEnumerable<PaymentOperationEvent> Events { get; } = events;
        public ProjectionCheckpoint Checkpoint { get; } = checkpoint;
    }
}
