using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal class RemovePaymentOperationCommand(PaymentOperation operationForDelete)
        : IRequest<Result<Guid>>
    {
        public PaymentOperation OperationForDelete { get; } = operationForDelete;
    }
}
