﻿using System;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.CQRS.Commands.Models;

namespace HomeBudget.Components.Accounts.CQRS.Commands.Handlers
{
    internal class UpdatePaymentAccountBalanceCommandHandler(IPaymentAccountDocumentClient paymentAccountDocumentClient)
        : IRequestHandler<UpdatePaymentAccountBalanceCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(
            UpdatePaymentAccountBalanceCommand request,
            CancellationToken cancellationToken)
        {
            var paymentAccountDocumentResult = await paymentAccountDocumentClient.GetByIdAsync(request.PaymentAccountId.ToString());
            var document = paymentAccountDocumentResult.Payload;
            var paymentAccountForUpdate = document.Payload;

            paymentAccountForUpdate.Balance = request.Balance;

            var updateResult = await paymentAccountDocumentClient.UpdateAsync(request.PaymentAccountId.ToString(), paymentAccountForUpdate);

            return new Result<Guid>(payload: updateResult.Payload);
        }
    }
}
