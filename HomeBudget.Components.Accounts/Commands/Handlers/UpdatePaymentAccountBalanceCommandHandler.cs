using System;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Accounts.Commands.Handlers
{
    internal class UpdatePaymentAccountBalanceCommandHandler(
        IPaymentAccountDocumentClient paymentAccountDocumentClient)
        : IRequestHandler<UpdatePaymentAccountBalanceCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(
            UpdatePaymentAccountBalanceCommand request,
            CancellationToken cancellationToken)
        {
            var paymentAccountDocumentResult = await paymentAccountDocumentClient.GetByIdAsync(request.PaymentAccountId.ToString());

            if (!paymentAccountDocumentResult.IsSucceeded)
            {
                return Result<Guid>.Failure($"Payment account: {request.PaymentAccountId} is null");
            }

            var document = paymentAccountDocumentResult.Payload;

            if (document == null)
            {
                return Result<Guid>.Failure($"Document for account: {request.PaymentAccountId} is null");
            }

            var paymentAccountForUpdate = document.Payload;

            paymentAccountForUpdate.Balance = request.Balance;

            var updateResult = await paymentAccountDocumentClient.UpdateAsync(request.PaymentAccountId.ToString(), paymentAccountForUpdate);

            return Result<Guid>.Succeeded(updateResult.Payload);
        }
    }
}
