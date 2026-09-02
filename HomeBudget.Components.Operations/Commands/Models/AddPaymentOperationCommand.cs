using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal sealed class AddPaymentOperationCommand(FinancialTransaction operationForAdd)
        : IRequest<Result<Guid>>, ICorrelatedCommand, IIdempotentPaymentCommand
    {
        public string CorrelationId { get; set; }
        public FinancialTransaction OperationForAdd { get; } = operationForAdd;

        public PaymentCommandContext CommandContext { get; init; }
    }
}
