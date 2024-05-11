using System;
using System.Collections.Generic;

using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Commands.Models
{
    internal class ApplyTransferCommand(CrossAccountsTransferOperation crossAccountsTransferOperations)
        : IRequest<Result<Guid>>
    {
        public Guid Key { get; } = crossAccountsTransferOperations.Key;

        public IReadOnlyCollection<PaymentOperation> PaymentOperations { get; } = crossAccountsTransferOperations.PaymentOperations;
    }
}
