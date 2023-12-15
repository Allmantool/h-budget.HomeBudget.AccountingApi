using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.CQRS.Commands.Models;

namespace HomeBudget.Components.Accounts.CQRS.Commands.Handlers
{
    internal class UpdatePaymentAccountBalanceCommandHandler
        : IRequestHandler<UpdatePaymentAccountBalanceCommand, Result<Guid>>
    {
        public Task<Result<Guid>> Handle(
            UpdatePaymentAccountBalanceCommand request,
            CancellationToken cancellationToken)
        {
            var paymentAccount = MockAccountsStore.Records.Single(pa => pa.Key.CompareTo(request.PaymentAccountId) == 0);

            paymentAccount.Balance = request.Balance;

            return Task.FromResult(new Result<Guid>(payload: paymentAccount.Key));
        }
    }
}
