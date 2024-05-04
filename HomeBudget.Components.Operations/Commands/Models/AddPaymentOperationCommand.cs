using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    public class AddPaymentOperationCommand(PaymentOperation operationForAdd)
        : IRequest<Result<Guid>>
    {
        public PaymentOperation OperationForAdd { get; } = operationForAdd;
    }
}
