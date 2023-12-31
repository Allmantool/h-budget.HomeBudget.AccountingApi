﻿using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.CQRS.Commands.Models
{
    public class UpdatePaymentOperationCommand(PaymentOperation operationForUpdate)
        : IRequest<Result<Guid>>
    {
        public PaymentOperation OperationForUpdate { get; } = operationForUpdate;
    }
}
