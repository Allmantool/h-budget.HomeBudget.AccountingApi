using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal class RemovePaymentOperationCommand(FinancialTransaction operationForDelete)
        : IRequest<Result<Guid>>, ICorrelatedCommand
    {
        public string CorrelationId { get; set; }

        public FinancialTransaction OperationForDelete { get; } = operationForDelete;
    }
}
