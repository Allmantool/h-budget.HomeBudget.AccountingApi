using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class UpdatePaymentOperationCommand(FinancialTransaction operationForUpdate)
        : IRequest<Result<Guid>>
    {
        public FinancialTransaction OperationForUpdate { get; } = operationForUpdate;
    }
}
