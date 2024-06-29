using System;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Accounts.Commands.Handlers
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

            return Result<Guid>.Succeeded(updateResult.Payload);
        }
    }
}
