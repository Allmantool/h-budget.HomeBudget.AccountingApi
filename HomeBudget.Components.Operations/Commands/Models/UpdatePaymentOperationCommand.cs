using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Commands;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class UpdatePaymentOperationCommand(FinancialTransaction operationForUpdate)
        : IRequest<Result<Guid>>, ICorrelatedCommand
    {
        public string CorrelationId { get; set; }

        public FinancialTransaction OperationForUpdate { get; } = operationForUpdate;
    }
}
