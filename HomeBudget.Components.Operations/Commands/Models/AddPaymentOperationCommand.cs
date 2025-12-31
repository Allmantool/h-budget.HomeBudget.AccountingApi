using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class AddPaymentOperationCommand(FinancialTransaction operationForAdd)
        : IRequest<Result<Guid>>, ICorrelatedCommand
    {
        public string CorrelationId { get; set; }

        public FinancialTransaction OperationForAdd { get; } = operationForAdd;
    }
}
