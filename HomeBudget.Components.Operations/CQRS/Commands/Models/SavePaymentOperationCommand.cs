using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.CQRS.Commands.Models
{
    public class SavePaymentOperationCommand(PaymentOperation operationForAdd)
        : IRequest<Result<Guid>>
    {
        public PaymentOperation OperationForAdd { get; } = operationForAdd;
    }
}
