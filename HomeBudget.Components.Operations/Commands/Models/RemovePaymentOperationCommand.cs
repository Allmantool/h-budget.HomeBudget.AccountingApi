﻿using System;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal class RemovePaymentOperationCommand(FinancialTransaction operationForDelete)
        : IRequest<Result<Guid>>
    {
        public FinancialTransaction OperationForDelete { get; } = operationForDelete;
    }
}
