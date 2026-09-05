using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal sealed class RemovePaymentOperationCommand(FinancialTransaction operationForDelete)
        : IRequest<Result<Guid>>, ICorrelatedCommand, IIdempotentPaymentCommand
    {
        public string CorrelationId { get; set; }

        public FinancialTransaction OperationForDelete { get; } = operationForDelete;

        public PaymentCommandContext CommandContext { get; init; }
    }
}
