using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class AddPaymentOperationCommand(FinancialTransaction operationForAdd)
        : IRequest<Result<Guid>>
    {
        public FinancialTransaction OperationForAdd { get; } = operationForAdd;
    }
}
